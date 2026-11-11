namespace AgfDocumentAi;

/// <summary>
/// Eén dunne laag tussen de software en een AI-model: een document plus de vaste instructie erin, de ruwe JSON-tekst
/// plus tokenverbruik en tijd eruit. Elke aanbieder is één kleine klasse hierachter; de controle van de uitvoer
/// (<see cref="ExtractionSchema.Validate"/>) gebeurt daarbuiten, voor alle modellen gelijk.
/// </summary>
public interface IDocumentAI
{
    Task<AiResponse> ExtractAsync(DocumentInput document, string instruction, CancellationToken cancellationToken);
}

/// <summary>Een PDF of afbeelding. <see cref="MediaType"/> is bijvoorbeeld application/pdf of image/jpeg.</summary>
public sealed record DocumentInput(string Name, byte[] Bytes, string MediaType)
{
    public bool IsPdf => MediaType == "application/pdf";

    public static string? MediaTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => null,
    };
}

/// <summary>Het antwoord van het model zoals het binnenkwam. <see cref="ModelVersion"/> is wat de aanbieder zelf terugmeldt.</summary>
public sealed record AiResponse(string Text, long InputTokens, long OutputTokens, TimeSpan Duration, string? ModelVersion);

/// <summary>Tijdelijke fout (te veel verzoeken, storing bij de aanbieder, time-out): opnieuw proberen heeft zin.</summary>
public sealed class TransientAiException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Dit model kan dit soort document niet lezen (bijvoorbeeld een PDF zonder omzetter naar afbeeldingen).</summary>
public sealed class UnsupportedDocumentException(string message) : Exception(message);
