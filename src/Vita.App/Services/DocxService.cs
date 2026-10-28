using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Vita.App.Services;

public sealed class DocxService
{
    public byte[] RenderResume(ResumeDocument doc, string accentHex)
    {
        var accent = accentHex.TrimStart('#');
        if (accent.Length != 6) accent = "1F6F5C";

        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            main.Document = new Document();
            var body = main.Document.AppendChild(new Body());

            AddParagraph(body, doc.Contact.FullName, size: 40, bold: true, spaceAfter: 20);
            if (!string.IsNullOrWhiteSpace(doc.Contact.Headline))
                AddParagraph(body, doc.Contact.Headline, size: 24, color: accent, spaceAfter: 40);

            var contact = new[] { doc.Contact.Email, doc.Contact.Phone, doc.Contact.Location, doc.Contact.LinkedIn, doc.Contact.Website, doc.Contact.Github }
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim());
            var contactLine = string.Join("  |  ", contact);
            if (contactLine.Length > 0)
                AddParagraph(body, contactLine, size: 18, color: "6E6A60", spaceAfter: 160);

            if (!string.IsNullOrWhiteSpace(doc.Summary))
            {
                AddHeading(body, "Resumo profissional", accent);
                AddParagraph(body, doc.Summary);
            }

            if (doc.Experience.Count > 0)
            {
                AddHeading(body, "Experiência profissional", accent);
                foreach (var exp in doc.Experience)
                {
                    var title = string.IsNullOrWhiteSpace(exp.PeriodLabel) ? exp.Role : $"{exp.Role}  |  {exp.PeriodLabel}";
                    AddParagraph(body, title, bold: true, spaceAfter: 10);
                    var company = exp.Company + (string.IsNullOrWhiteSpace(exp.Location) ? "" : " — " + exp.Location);
                    AddParagraph(body, company, size: 20, color: accent, spaceAfter: 30);
                    foreach (var bullet in exp.Bullets)
                        AddBullet(body, bullet);
                    AddParagraph(body, string.Empty, spaceAfter: 60);
                }
            }

            if (doc.SkillGroups.Count > 0)
            {
                AddHeading(body, "Competências", accent);
                foreach (var group in doc.SkillGroups)
                    AddParagraph(body, $"{group.Name}: {string.Join(", ", group.Items)}", spaceAfter: 40);
            }

            if (doc.Education.Count > 0)
            {
                AddHeading(body, "Formação acadêmica", accent);
                foreach (var edu in doc.Education)
                {
                    AddParagraph(body, $"{edu.Degree} {edu.Field}".Trim(), bold: true, spaceAfter: 10);
                    AddParagraph(body, $"{edu.Institution}  |  {edu.PeriodLabel}".Trim(), size: 20, color: "6E6A60", spaceAfter: 50);
                }
            }

            if (doc.Projects.Count > 0)
            {
                AddHeading(body, "Projetos", accent);
                foreach (var project in doc.Projects)
                {
                    AddParagraph(body, project.Name, bold: true, spaceAfter: 10);
                    if (!string.IsNullOrWhiteSpace(project.Description)) AddParagraph(body, project.Description, spaceAfter: 20);
                    foreach (var bullet in project.Bullets) AddBullet(body, bullet);
                }
            }

            if (doc.Certifications.Count > 0)
            {
                AddHeading(body, "Certificações", accent);
                foreach (var cert in doc.Certifications)
                    AddParagraph(body, $"{cert.Name} — {cert.Issuer} {cert.Year}".Trim(), spaceAfter: 20);
            }

            if (doc.Languages.Count > 0)
            {
                AddHeading(body, "Idiomas", accent);
                AddParagraph(body, string.Join("  |  ", doc.Languages.Select(l => $"{l.Name} ({l.Level})")));
            }

            if (doc.CustomSections.Count > 0)
            {
                foreach (var section in doc.CustomSections)
                {
                    AddHeading(body, section.Title, accent);
                    foreach (var item in section.Items) AddBullet(body, item);
                }
            }

            body.Append(new SectionProperties(
                new PageSize { Width = 11906, Height = 16838 },
                new PageMargin { Top = 1020, Bottom = 1020, Left = 1134, Right = 1134 }));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static void AddHeading(Body body, string text, string accent)
    {
        var properties = new ParagraphProperties(
            new SpacingBetweenLines { Before = "280", After = "80" },
            new ParagraphBorders(new BottomBorder { Val = BorderValues.Single, Size = 6, Color = accent, Space = 2 }));
        var run = new Run(new RunProperties
        {
            Bold = new Bold(),
            FontSize = new FontSize { Val = "19" },
            Color = new Color { Val = accent },
            Caps = new Caps()
        }, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        body.Append(new Paragraph(properties, run));
    }

    private static void AddBullet(Body body, string text)
    {
        var properties = new ParagraphProperties(
            new SpacingBetweenLines { After = "40", Line = "264", LineRule = LineSpacingRuleValues.Auto },
            new Indentation { Left = "360", Hanging = "220" });
        var run = new Run(new RunProperties { FontSize = new FontSize { Val = "20" } },
            new Text("•  " + text) { Space = SpaceProcessingModeValues.Preserve });
        body.Append(new Paragraph(properties, run));
    }

    private static void AddParagraph(Body body, string text, int size = 20, bool bold = false,
        string? color = null, int spaceAfter = 60)
    {
        var runProperties = new RunProperties { FontSize = new FontSize { Val = size.ToString() } };
        if (bold) runProperties.Bold = new Bold();
        if (!string.IsNullOrWhiteSpace(color)) runProperties.Color = new Color { Val = color };
        var properties = new ParagraphProperties(
            new SpacingBetweenLines { After = spaceAfter.ToString(), Line = "264", LineRule = LineSpacingRuleValues.Auto });
        var paragraph = new Paragraph(properties, new Run(runProperties,
            new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        body.Append(paragraph);
    }
}
