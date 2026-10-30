using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddCountries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OriginCountryCode",
                table: "ProductVariants",
                type: "character(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Countries",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    NameNl = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "CountryAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Alias = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AliasKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CountryCode = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    ActorReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CountryAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CountryAliases_Countries_CountryCode",
                        column: x => x.CountryCode,
                        principalTable: "Countries",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Countries",
                columns: new[] { "Code", "NameEn", "NameNl" },
                values: new object[,]
                {
                    { "AD", "Andorra", "Andorra" },
                    { "AE", "United Arab Emirates", "Verenigde Arabische Emiraten" },
                    { "AF", "Afghanistan", "Afghanistan" },
                    { "AG", "Antigua & Barbuda", "Antigua en Barbuda" },
                    { "AI", "Anguilla", "Anguilla" },
                    { "AL", "Albania", "Albanië" },
                    { "AM", "Armenia", "Armenië" },
                    { "AO", "Angola", "Angola" },
                    { "AQ", "Antarctica", "Antarctica" },
                    { "AR", "Argentina", "Argentinië" },
                    { "AS", "American Samoa", "Amerikaans-Samoa" },
                    { "AT", "Austria", "Oostenrijk" },
                    { "AU", "Australia", "Australië" },
                    { "AW", "Aruba", "Aruba" },
                    { "AX", "Åland Islands", "Åland" },
                    { "AZ", "Azerbaijan", "Azerbeidzjan" },
                    { "BA", "Bosnia & Herzegovina", "Bosnië en Herzegovina" },
                    { "BB", "Barbados", "Barbados" },
                    { "BD", "Bangladesh", "Bangladesh" },
                    { "BE", "Belgium", "België" },
                    { "BF", "Burkina Faso", "Burkina Faso" },
                    { "BG", "Bulgaria", "Bulgarije" },
                    { "BH", "Bahrain", "Bahrein" },
                    { "BI", "Burundi", "Burundi" },
                    { "BJ", "Benin", "Benin" },
                    { "BL", "St. Barthélemy", "Saint-Barthélemy" },
                    { "BM", "Bermuda", "Bermuda" },
                    { "BN", "Brunei", "Brunei" },
                    { "BO", "Bolivia", "Bolivia" },
                    { "BQ", "Caribbean Netherlands", "Caribisch Nederland" },
                    { "BR", "Brazil", "Brazilië" },
                    { "BS", "Bahamas", "Bahama's" },
                    { "BT", "Bhutan", "Bhutan" },
                    { "BV", "Bouvet Island", "Bouveteiland" },
                    { "BW", "Botswana", "Botswana" },
                    { "BY", "Belarus", "Belarus" },
                    { "BZ", "Belize", "Belize" },
                    { "CA", "Canada", "Canada" },
                    { "CC", "Cocos (Keeling) Islands", "Cocoseilanden" },
                    { "CD", "Congo - Kinshasa", "Congo-Kinshasa" },
                    { "CF", "Central African Republic", "Centraal-Afrikaanse Republiek" },
                    { "CG", "Congo - Brazzaville", "Congo-Brazzaville" },
                    { "CH", "Switzerland", "Zwitserland" },
                    { "CI", "Côte d'Ivoire", "Ivoorkust" },
                    { "CK", "Cook Islands", "Cookeilanden" },
                    { "CL", "Chile", "Chili" },
                    { "CM", "Cameroon", "Kameroen" },
                    { "CN", "China", "China" },
                    { "CO", "Colombia", "Colombia" },
                    { "CR", "Costa Rica", "Costa Rica" },
                    { "CU", "Cuba", "Cuba" },
                    { "CV", "Cape Verde", "Kaapverdië" },
                    { "CW", "Curaçao", "Curaçao" },
                    { "CX", "Christmas Island", "Christmaseiland" },
                    { "CY", "Cyprus", "Cyprus" },
                    { "CZ", "Czechia", "Tsjechië" },
                    { "DE", "Germany", "Duitsland" },
                    { "DJ", "Djibouti", "Djibouti" },
                    { "DK", "Denmark", "Denemarken" },
                    { "DM", "Dominica", "Dominica" },
                    { "DO", "Dominican Republic", "Dominicaanse Republiek" },
                    { "DZ", "Algeria", "Algerije" },
                    { "EC", "Ecuador", "Ecuador" },
                    { "EE", "Estonia", "Estland" },
                    { "EG", "Egypt", "Egypte" },
                    { "EH", "Western Sahara", "Westelijke Sahara" },
                    { "ER", "Eritrea", "Eritrea" },
                    { "ES", "Spain", "Spanje" },
                    { "ET", "Ethiopia", "Ethiopië" },
                    { "FI", "Finland", "Finland" },
                    { "FJ", "Fiji", "Fiji" },
                    { "FK", "Falkland Islands", "Falklandeilanden" },
                    { "FM", "Micronesia", "Micronesia" },
                    { "FO", "Faroe Islands", "Faeröer" },
                    { "FR", "France", "Frankrijk" },
                    { "GA", "Gabon", "Gabon" },
                    { "GB", "United Kingdom", "Verenigd Koninkrijk" },
                    { "GD", "Grenada", "Grenada" },
                    { "GE", "Georgia", "Georgië" },
                    { "GF", "French Guiana", "Frans-Guyana" },
                    { "GG", "Guernsey", "Guernsey" },
                    { "GH", "Ghana", "Ghana" },
                    { "GI", "Gibraltar", "Gibraltar" },
                    { "GL", "Greenland", "Groenland" },
                    { "GM", "Gambia", "Gambia" },
                    { "GN", "Guinea", "Guinee" },
                    { "GP", "Guadeloupe", "Guadeloupe" },
                    { "GQ", "Equatorial Guinea", "Equatoriaal-Guinea" },
                    { "GR", "Greece", "Griekenland" },
                    { "GS", "South Georgia & South Sandwich Islands", "Zuid-Georgia en Zuidelijke Sandwicheilanden" },
                    { "GT", "Guatemala", "Guatemala" },
                    { "GU", "Guam", "Guam" },
                    { "GW", "Guinea-Bissau", "Guinee-Bissau" },
                    { "GY", "Guyana", "Guyana" },
                    { "HK", "Hong Kong SAR China", "Hongkong SAR van China" },
                    { "HM", "Heard & McDonald Islands", "Heard en McDonaldeilanden" },
                    { "HN", "Honduras", "Honduras" },
                    { "HR", "Croatia", "Kroatië" },
                    { "HT", "Haiti", "Haïti" },
                    { "HU", "Hungary", "Hongarije" },
                    { "ID", "Indonesia", "Indonesië" },
                    { "IE", "Ireland", "Ierland" },
                    { "IL", "Israel", "Israël" },
                    { "IM", "Isle of Man", "Isle of Man" },
                    { "IN", "India", "India" },
                    { "IO", "British Indian Ocean Territory", "Brits Indische Oceaanterritorium" },
                    { "IQ", "Iraq", "Irak" },
                    { "IR", "Iran", "Iran" },
                    { "IS", "Iceland", "IJsland" },
                    { "IT", "Italy", "Italië" },
                    { "JE", "Jersey", "Jersey" },
                    { "JM", "Jamaica", "Jamaica" },
                    { "JO", "Jordan", "Jordanië" },
                    { "JP", "Japan", "Japan" },
                    { "KE", "Kenya", "Kenia" },
                    { "KG", "Kyrgyzstan", "Kirgizië" },
                    { "KH", "Cambodia", "Cambodja" },
                    { "KI", "Kiribati", "Kiribati" },
                    { "KM", "Comoros", "Comoren" },
                    { "KN", "St. Kitts & Nevis", "Saint Kitts en Nevis" },
                    { "KP", "North Korea", "Noord-Korea" },
                    { "KR", "South Korea", "Zuid-Korea" },
                    { "KW", "Kuwait", "Koeweit" },
                    { "KY", "Cayman Islands", "Kaaimaneilanden" },
                    { "KZ", "Kazakhstan", "Kazachstan" },
                    { "LA", "Laos", "Laos" },
                    { "LB", "Lebanon", "Libanon" },
                    { "LC", "St. Lucia", "Saint Lucia" },
                    { "LI", "Liechtenstein", "Liechtenstein" },
                    { "LK", "Sri Lanka", "Sri Lanka" },
                    { "LR", "Liberia", "Liberia" },
                    { "LS", "Lesotho", "Lesotho" },
                    { "LT", "Lithuania", "Litouwen" },
                    { "LU", "Luxembourg", "Luxemburg" },
                    { "LV", "Latvia", "Letland" },
                    { "LY", "Libya", "Libië" },
                    { "MA", "Morocco", "Marokko" },
                    { "MC", "Monaco", "Monaco" },
                    { "MD", "Moldova", "Moldavië" },
                    { "ME", "Montenegro", "Montenegro" },
                    { "MF", "St. Martin", "Saint-Martin" },
                    { "MG", "Madagascar", "Madagaskar" },
                    { "MH", "Marshall Islands", "Marshalleilanden" },
                    { "MK", "North Macedonia", "Noord-Macedonië" },
                    { "ML", "Mali", "Mali" },
                    { "MM", "Myanmar (Burma)", "Myanmar (Birma)" },
                    { "MN", "Mongolia", "Mongolië" },
                    { "MO", "Macao SAR China", "Macau SAR van China" },
                    { "MP", "Northern Mariana Islands", "Noordelijke Marianen" },
                    { "MQ", "Martinique", "Martinique" },
                    { "MR", "Mauritania", "Mauritanië" },
                    { "MS", "Montserrat", "Montserrat" },
                    { "MT", "Malta", "Malta" },
                    { "MU", "Mauritius", "Mauritius" },
                    { "MV", "Maldives", "Maldiven" },
                    { "MW", "Malawi", "Malawi" },
                    { "MX", "Mexico", "Mexico" },
                    { "MY", "Malaysia", "Maleisië" },
                    { "MZ", "Mozambique", "Mozambique" },
                    { "NA", "Namibia", "Namibië" },
                    { "NC", "New Caledonia", "Nieuw-Caledonië" },
                    { "NE", "Niger", "Niger" },
                    { "NF", "Norfolk Island", "Norfolk" },
                    { "NG", "Nigeria", "Nigeria" },
                    { "NI", "Nicaragua", "Nicaragua" },
                    { "NL", "Netherlands", "Nederland" },
                    { "NO", "Norway", "Noorwegen" },
                    { "NP", "Nepal", "Nepal" },
                    { "NR", "Nauru", "Nauru" },
                    { "NU", "Niue", "Niue" },
                    { "NZ", "New Zealand", "Nieuw-Zeeland" },
                    { "OM", "Oman", "Oman" },
                    { "PA", "Panama", "Panama" },
                    { "PE", "Peru", "Peru" },
                    { "PF", "French Polynesia", "Frans-Polynesië" },
                    { "PG", "Papua New Guinea", "Papoea-Nieuw-Guinea" },
                    { "PH", "Philippines", "Filipijnen" },
                    { "PK", "Pakistan", "Pakistan" },
                    { "PL", "Poland", "Polen" },
                    { "PM", "St. Pierre & Miquelon", "Saint-Pierre en Miquelon" },
                    { "PN", "Pitcairn Islands", "Pitcairneilanden" },
                    { "PR", "Puerto Rico", "Puerto Rico" },
                    { "PS", "Palestinian Territories", "Palestijnse gebieden" },
                    { "PT", "Portugal", "Portugal" },
                    { "PW", "Palau", "Palau" },
                    { "PY", "Paraguay", "Paraguay" },
                    { "QA", "Qatar", "Qatar" },
                    { "RE", "Réunion", "Réunion" },
                    { "RO", "Romania", "Roemenië" },
                    { "RS", "Serbia", "Servië" },
                    { "RU", "Russia", "Rusland" },
                    { "RW", "Rwanda", "Rwanda" },
                    { "SA", "Saudi Arabia", "Saoedi-Arabië" },
                    { "SB", "Solomon Islands", "Salomonseilanden" },
                    { "SC", "Seychelles", "Seychellen" },
                    { "SD", "Sudan", "Soedan" },
                    { "SE", "Sweden", "Zweden" },
                    { "SG", "Singapore", "Singapore" },
                    { "SH", "St. Helena", "Sint-Helena" },
                    { "SI", "Slovenia", "Slovenië" },
                    { "SJ", "Svalbard & Jan Mayen", "Spitsbergen en Jan Mayen" },
                    { "SK", "Slovakia", "Slowakije" },
                    { "SL", "Sierra Leone", "Sierra Leone" },
                    { "SM", "San Marino", "San Marino" },
                    { "SN", "Senegal", "Senegal" },
                    { "SO", "Somalia", "Somalië" },
                    { "SR", "Suriname", "Suriname" },
                    { "SS", "South Sudan", "Zuid-Soedan" },
                    { "ST", "São Tomé & Príncipe", "Sao Tomé en Principe" },
                    { "SV", "El Salvador", "El Salvador" },
                    { "SX", "Sint Maarten", "Sint-Maarten" },
                    { "SY", "Syria", "Syrië" },
                    { "SZ", "Eswatini", "Eswatini" },
                    { "TC", "Turks & Caicos Islands", "Turks- en Caicoseilanden" },
                    { "TD", "Chad", "Tsjaad" },
                    { "TF", "French Southern Territories", "Franse Gebieden in de zuidelijke Indische Oceaan" },
                    { "TG", "Togo", "Togo" },
                    { "TH", "Thailand", "Thailand" },
                    { "TJ", "Tajikistan", "Tadzjikistan" },
                    { "TK", "Tokelau", "Tokelau" },
                    { "TL", "Timor-Leste", "Oost-Timor" },
                    { "TM", "Turkmenistan", "Turkmenistan" },
                    { "TN", "Tunisia", "Tunesië" },
                    { "TO", "Tonga", "Tonga" },
                    { "TR", "Türkiye", "Turkije" },
                    { "TT", "Trinidad & Tobago", "Trinidad en Tobago" },
                    { "TV", "Tuvalu", "Tuvalu" },
                    { "TW", "Taiwan", "Taiwan" },
                    { "TZ", "Tanzania", "Tanzania" },
                    { "UA", "Ukraine", "Oekraïne" },
                    { "UG", "Uganda", "Oeganda" },
                    { "UM", "U.S. Outlying Islands", "Kleine afgelegen eilanden van de Verenigde Staten" },
                    { "US", "United States", "Verenigde Staten" },
                    { "UY", "Uruguay", "Uruguay" },
                    { "UZ", "Uzbekistan", "Oezbekistan" },
                    { "VA", "Vatican City", "Vaticaanstad" },
                    { "VC", "St. Vincent & Grenadines", "Saint Vincent en de Grenadines" },
                    { "VE", "Venezuela", "Venezuela" },
                    { "VG", "British Virgin Islands", "Britse Maagdeneilanden" },
                    { "VI", "U.S. Virgin Islands", "Amerikaanse Maagdeneilanden" },
                    { "VN", "Vietnam", "Vietnam" },
                    { "VU", "Vanuatu", "Vanuatu" },
                    { "WF", "Wallis & Futuna", "Wallis en Futuna" },
                    { "WS", "Samoa", "Samoa" },
                    { "YE", "Yemen", "Jemen" },
                    { "YT", "Mayotte", "Mayotte" },
                    { "ZA", "South Africa", "Zuid-Afrika" },
                    { "ZM", "Zambia", "Zambia" },
                    { "ZW", "Zimbabwe", "Zimbabwe" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_OriginCountryCode",
                table: "ProductVariants",
                column: "OriginCountryCode");

            migrationBuilder.CreateIndex(
                name: "IX_CountryAliases_AliasKey",
                table: "CountryAliases",
                column: "AliasKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CountryAliases_CountryCode",
                table: "CountryAliases",
                column: "CountryCode");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariants_Countries_OriginCountryCode",
                table: "ProductVariants",
                column: "OriginCountryCode",
                principalTable: "Countries",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);

            // Bestaande herkomstteksten alleen koppelen als ze eenduidig één land zijn: exact de ISO-code, de Nederlandse of de Engelse
            // naam, zonder onderscheid in hoofdletters, accenten en dubbele spaties ("PE", "peru", "Peru" en "Brazilie" -> land). Zo'n
            // variant krijgt de landcode en als weergavetekst de Nederlandse naam. Al het andere ("Peru (DEMO)", "EU", "Divers", leeg)
            // blijft ongewijzigd en zonder code (Artikelen > Herkomst opschonen). C#-spiegel: CountryRules.Unambiguous.
            migrationBuilder.Sql(@"
WITH variant_keys AS (
    SELECT v.""Id"", translate(regexp_replace(lower(trim(v.""Origin"")), '\s+', ' ', 'g'), 'áàâäãåéèêëíìîïóòôöõúùûüýÿçñ', 'aaaaaaeeeeiiiiooooouuuuyycn') AS key
    FROM ""ProductVariants"" v
    WHERE v.""OriginCountryCode"" IS NULL AND v.""Origin"" IS NOT NULL AND trim(v.""Origin"") <> ''
), country_keys AS (
    SELECT c.""Code"", k.key
    FROM ""Countries"" c
    CROSS JOIN LATERAL (VALUES (lower(c.""Code"")), (lower(c.""NameNl"")), (lower(c.""NameEn""))) AS raw(value)
    CROSS JOIN LATERAL (SELECT translate(regexp_replace(trim(raw.value), '\s+', ' ', 'g'), 'áàâäãåéèêëíìîïóòôöõúùûüýÿçñ', 'aaaaaaeeeeiiiiooooouuuuyycn') AS key) AS k
), matches AS (
    SELECT vk.""Id"", min(ck.""Code"") AS code, count(DISTINCT ck.""Code"") AS countries
    FROM variant_keys vk JOIN country_keys ck ON ck.key = vk.key
    GROUP BY vk.""Id""
)
UPDATE ""ProductVariants"" v
SET ""OriginCountryCode"" = m.code, ""Origin"" = c.""NameNl""
FROM matches m JOIN ""Countries"" c ON c.""Code"" = m.code
WHERE v.""Id"" = m.""Id"" AND m.countries = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariants_Countries_OriginCountryCode",
                table: "ProductVariants");

            migrationBuilder.DropTable(
                name: "CountryAliases");

            migrationBuilder.DropTable(
                name: "Countries");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_OriginCountryCode",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "OriginCountryCode",
                table: "ProductVariants");
        }
    }
}
