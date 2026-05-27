using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace AgfAdmin.Services
{
    public class SimpleSalesParserService : ISalesParserService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SimpleSalesParserService> _logger;

        public SimpleSalesParserService(IServiceScopeFactory scopeFactory, ILogger<SimpleSalesParserService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<ParseOrderResponse> ParseOrderTextAsync(string rawText)
        {
            var response = new ParseOrderResponse();
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // 1. Fetch all variants for matching (cache this in production!)
            var variants = await context.ProductVariants
                .Include(v => v.Product)
                .ToListAsync();

            // Fetch aggregate stock levels
            var inventoryDict = await context.Inventory
                .GroupBy(i => i.ProductVariantId)
                .Select(g => new { ProductVariantId = g.Key, TotalQuantity = g.Sum(i => i.Quantity) })
                .ToDictionaryAsync(k => k.ProductVariantId, v => v.TotalQuantity);

            // Pre-process: replace common natural language separators with commas
            var preProcessedText = rawText
                .Replace(" and ", ",", StringComparison.OrdinalIgnoreCase)
                .Replace(" en ", ",", StringComparison.OrdinalIgnoreCase)
                .Replace(" + ", ",", StringComparison.OrdinalIgnoreCase);

            // 2. Split text into potential lines/items
            // Split by newlines, commas, or semicolons
            var segments = preProcessedText.Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var segment in segments)
            {
                var cleanSegment = segment.Trim();
                if (string.IsNullOrWhiteSpace(cleanSegment)) continue;
                if (cleanSegment.ToLower() == "and" || cleanSegment.ToLower() == "en") continue;

                var item = ParseSegment(cleanSegment, variants, inventoryDict, context);
                response.Items.Add(item);
            }

            // Calculate overall confidence (average of items)
            if (response.Items.Any())
            {
                response.ConfidenceScore = response.Items.Average(i => i.Confidence);
            }
            else
            {
                response.ConfidenceScore = 0;
                response.Warnings.Add("No items could be parsed from the text.");
            }

            return response;
        }

        private ParsedOrderItem ParseSegment(string segment, List<ProductVariant> variants, Dictionary<int, int> inventoryDict, AppDbContext context)
        {
            var item = new ParsedOrderItem
            {
                OriginalTextSegment = segment,
                Confidence = 0.0
            };

            var originalTextLower = segment.ToLowerInvariant();

            // Extract Size Category (e.g. "size 10", "maat 10", "cal 10")
            var sizeRegex = new Regex(@"\b(size|maat|cal|kaliber)\s*(\d+)\b", RegexOptions.IgnoreCase);
            var sizeMatch = sizeRegex.Match(segment);
            string? detectedSize = null;
            if (sizeMatch.Success)
            {
                detectedSize = sizeMatch.Groups[2].Value; // Just the number
                segment = segment.Replace(sizeMatch.Value, "").Trim();
            }

            // Regex to find quantity strictly at the START of the phrase, handling decimals or simple ints + x
            // e.g. "5", "5.5", "5x", "5 kg", "10 boxes"
            // We use ^ to ensure it only grabs the starting number, avoiding grabbing random numbers inside the string later
            var qtyRegex = new Regex(@"^(\d+[\.,]?\d*)\s*([xX]|stuks|pieces)?\b");
            var match = qtyRegex.Match(segment);

            if (match.Success)
            {
                if (decimal.TryParse(match.Groups[1].Value.Replace(',', '.'), out decimal qtyDecimal))
                {
                    item.Quantity = (int)Math.Round(qtyDecimal); // Simplifying to int for now based on DTO
                    segment = segment.Replace(match.Value, "").Trim();
                }
            }
            else
            {
                // Fallback: try capturing any freestanding number if no starting number was found
                var fallbackQtyRegex = new Regex(@"\b(\d+)\s*([xX])?\b");
                var fallbackMatch = fallbackQtyRegex.Match(segment);
                if (fallbackMatch.Success && int.TryParse(fallbackMatch.Groups[1].Value, out int fQty))
                {
                    item.Quantity = fQty;
                    segment = segment.Replace(fallbackMatch.Value, "").Trim();
                }
                else
                {
                    item.Quantity = 1; // Default to 1 if not specified
                    item.Confidence -= 0.2; // Lower confidence
                }
            }

            // --- Lightning Shortcut Override ---
            var potentialShortcut = segment.Trim().ToLowerInvariant();
            var shortcutVariant = variants.FirstOrDefault(v => !string.IsNullOrEmpty(v.ShortcutCode) && v.ShortcutCode.ToLowerInvariant() == potentialShortcut);
            
            if (shortcutVariant != null)
            {
                // Perfect shortcut hit - bypass all NLP!
                item.ProductVariantId = shortcutVariant.Id;
                item.ProductName = $"{shortcutVariant.Product?.Name} {shortcutVariant.Name}";
                item.EstimatedPrice = shortcutVariant.DefaultPrice;
                item.Confidence = 1.0;
                
                if (inventoryDict.TryGetValue(shortcutVariant.Id, out int totalInv))
                {
                    if (totalInv < item.Quantity)
                    {
                        item.Confidence -= 0.1;
                    }
                }
                else
                {
                     item.Confidence -= 0.1;
                }

                return item;
            }
            // --- End Lightning Shortcut ---

            // Detect unit (basic)
            if (Regex.IsMatch(segment, @"\b(box(es)?|doos|dozen)\b", RegexOptions.IgnoreCase))
            {
                item.Unit = "Box";
                segment = Regex.Replace(segment, @"\b(box(es)?|doos|dozen)\b", "", RegexOptions.IgnoreCase).Trim();
            }
            else if (Regex.IsMatch(segment, @"\b(kg|kilo)\b", RegexOptions.IgnoreCase))
            {
                item.Unit = "Kg";
                segment = Regex.Replace(segment, @"\b(kg|kilo)\b", "", RegexOptions.IgnoreCase).Trim();
            }

            // Clean up left over fragments
            segment = Regex.Replace(segment, @"\b(of|van|with|met|and|en)\b", "", RegexOptions.IgnoreCase).Trim();
            segment = Regex.Replace(segment, @"\s+", " "); // Condense spaces

            // Match remaining text to ProductVariant
            var bestMatch = FindBestVariant(segment, detectedSize, variants, out bool isAmbiguousKnownProduct);
            
            if (bestMatch != null)
            {
                item.ProductVariantId = bestMatch.Id;
                item.ProductName = $"{bestMatch.Product.Name} {bestMatch.Name}";
                item.EstimatedPrice = bestMatch.DefaultPrice;
                item.Confidence += 0.8; // High confidence if found
                item.CurrentStock = inventoryDict.GetValueOrDefault(bestMatch.Id, 0);

                // If we also matched the correct size dynamically, boost confidence
                if (detectedSize != null && bestMatch.SizeCategory?.Contains(detectedSize) == true)
                {
                    item.Confidence = 1.0;
                }
            }
            else if (isAmbiguousKnownProduct)
            {
                // The user typed a known base product (e.g. Avocado) but did NOT specify which variant (Hass vs Fuerte).
                // Do NOT auto-create a ghost variant. Return null so the UI forces them to use the combobox.
                item.ProductVariantId = null;
                item.ProductName = null;
                item.EstimatedPrice = null;
                item.Confidence = 0.0;
                item.CurrentStock = 0;
            }
            else
            {
                // Auto-create entirely missing product
                var safeName = string.IsNullOrWhiteSpace(segment) ? "Unknown" : char.ToUpper(segment[0]) + segment.Substring(1);
                var newProduct = new Product
                {
                    Name = safeName,
                    ImageUrl = ""
                };
                context.Products.Add(newProduct);
                context.SaveChanges(); 

                var newVariant = new ProductVariant
                {
                    ProductId = newProduct.Id,
                    Name = detectedSize != null ? $"Size {detectedSize}" : "Standard",
                    Origin = "Unknown",
                    SizeCategory = detectedSize ?? "Standard",
                    Packaging = item.Unit ?? "Unit",
                    DefaultPrice = 0m
                };
                context.ProductVariants.Add(newVariant);
                context.SaveChanges();

                newVariant.Product = newProduct;
                variants.Add(newVariant);

                item.ProductVariantId = newVariant.Id;
                item.ProductName = $"{newProduct.Name} {newVariant.Name}"; 
                item.EstimatedPrice = 0m;
                item.Confidence = 0.9;
                item.CurrentStock = 0;
            }

            return item;
        }

        private ProductVariant? FindBestVariant(string searchText, string? detectedSize, List<ProductVariant> variants, out bool isAmbiguousKnownProduct)
        {
            isAmbiguousKnownProduct = false;
            if (string.IsNullOrWhiteSpace(searchText)) return null;
            searchText = searchText.ToLowerInvariant();

            // 1. Find all variants where the Product name is explicitly mentioned in the text
            // e.g., if searchText is "avocado", it matches Product "Avocado"
            var matchingProducts = variants.Where(v => searchText.Contains(v.Product.Name.ToLowerInvariant())).ToList();

            if (matchingProducts.Any())
            {
                // We found the base product. Did they mention a specific variant?
                var specificVariantMatch = matchingProducts.Where(v => 
                    searchText.Contains(v.Name.ToLowerInvariant()) 
                ).ToList();

                if (specificVariantMatch.Any())
                {
                    if (detectedSize != null)
                    {
                        var sizedMatch = specificVariantMatch.FirstOrDefault(v => v.SizeCategory != null && v.SizeCategory.Contains(detectedSize));
                        if (sizedMatch != null) return sizedMatch;
                    }
                    return specificVariantMatch.First();
                }

                // They didn't mention the variant name. Did they mention a size that natively matches a variant?
                if (detectedSize != null)
                {
                    var sizedMatch = matchingProducts.FirstOrDefault(v => v.SizeCategory != null && v.SizeCategory.Contains(detectedSize));
                    if (sizedMatch != null) return sizedMatch;
                }

                // If the product only has exactly 1 valid variant historically, we can safely assume it!
                if (matchingProducts.Select(v => v.Id).Distinct().Count() == 1)
                {
                    return matchingProducts.First();
                }

                // Ambiguous! Known product, unknown/missing variant, multiple choices exist.
                isAmbiguousKnownProduct = true;
                return null;
            }

            // 2. Completely unknown product, or they typed a weird alias. Try loose contains.
            var looseMatch = variants.FirstOrDefault(v => v.Name.ToLowerInvariant().Contains(searchText) || searchText.Contains(v.Name.ToLowerInvariant()));
            if (looseMatch != null) return looseMatch;

            return null; // Entirely ambiguous novel product.
        }
    }
}
