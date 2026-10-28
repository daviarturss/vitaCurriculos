using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Vita.App.Services;

public sealed class PdfService
{
    private const string Ink = "#191914";
    private const string Muted = "#6E6A60";
    private const string Line = "#DED9CD";

    public PdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseEnvironmentFonts = true;
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;
    }

    public byte[] RenderResume(ResumeDocument doc, string templateId, string accentHex)
    {
        var accent = SafeColor(accentHex);
        var bytes = templateId switch
        {
            "classico" => BuildClassic(doc, accent),
            "minimal" => BuildMinimal(doc, accent),
            "compacto" => BuildCompact(doc, accent),
            _ => BuildAurora(doc, accent)
        };
        return bytes;
    }

    public byte[] RenderCoverLetter(ResumeDocument doc, string body, string accentHex)
    {
        var accent = SafeColor(accentHex);
        var paragraphs = body.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim()).Where(p => p.Length > 0).ToList();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(56);
                page.DefaultTextStyle(x => x.FontFamily(DefaultFont).FontSize(10.5f).FontColor(Ink).LineHeight(1.5f));

                page.Content().Column(col =>
                {
                    col.Item().Text(doc.Contact.FullName).FontSize(19).Bold().FontColor(accent);
                    var contact = ContactParts(doc);
                    if (contact.Count > 0)
                        col.Item().PaddingTop(3).Text(string.Join("  ·  ", contact)).FontSize(8.5f).FontColor(Muted);
                    col.Item().PaddingTop(10).BorderBottom(1).BorderColor(accent);
                    col.Item().PaddingTop(22);

                    foreach (var paragraph in paragraphs)
                        col.Item().PaddingBottom(12).Text(paragraph);
                });
            });
        }).GeneratePdf();
    }

    private static string DefaultFont => OperatingSystem.IsWindows() ? "Segoe UI" : "DejaVu Sans";
    private static string SerifFont => OperatingSystem.IsWindows() ? "Georgia" : "DejaVu Serif";

    // ---------------------------------------------------------------- Aurora
    private static byte[] BuildAurora(ResumeDocument doc, string accent) => Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.DefaultTextStyle(x => x.FontFamily(DefaultFont).FontSize(9.2f).FontColor(Ink).LineHeight(1.4f));

            page.Content().Column(root =>
            {
                root.Item().Background(accent).PaddingHorizontal(38).PaddingVertical(28).Column(header =>
                {
                    header.Item().Text(doc.Contact.FullName).FontSize(25).Bold().FontColor("#FFFFFF");
                    if (!string.IsNullOrWhiteSpace(doc.Contact.Headline))
                        header.Item().PaddingTop(2).Text(doc.Contact.Headline).FontSize(11).FontColor("#E8F3EE");
                    var contact = ContactParts(doc);
                    if (contact.Count > 0)
                        header.Item().PaddingTop(9).Text(string.Join("   ·   ", contact)).FontSize(8.2f).FontColor("#D6E9E1");
                });

                root.Item().PaddingHorizontal(38).PaddingVertical(24).Row(row =>
                {
                    row.RelativeItem(2.05f).PaddingRight(22).Column(main =>
                    {
                        if (!string.IsNullOrWhiteSpace(doc.Summary))
                        {
                            SectionHeader(main, "Perfil", accent, 0);
                            main.Item().Text(doc.Summary);
                        }

                        if (doc.Experience.Count > 0)
                        {
                            SectionHeader(main, "Experiência", accent);
                            foreach (var exp in doc.Experience)
                            {
                                main.Item().PaddingBottom(9).Column(item =>
                                {
                                    item.Item().Row(r =>
                                    {
                                        r.RelativeItem().Text(exp.Role).Bold().FontSize(10);
                                        if (!string.IsNullOrWhiteSpace(exp.PeriodLabel))
                                            r.ConstantItem(92).AlignRight().Text(exp.PeriodLabel).FontSize(8).FontColor(Muted);
                                    });
                                    item.Item().Text($"{exp.Company}{(string.IsNullOrWhiteSpace(exp.Location) ? "" : " · " + exp.Location)}").FontSize(8.7f).FontColor(accent).SemiBold();
                                    foreach (var bullet in exp.Bullets.Where(b => !string.IsNullOrWhiteSpace(b)))
                                        item.Item().PaddingTop(2.5f).PaddingLeft(2).Text(text =>
                                        {
                                            text.Span("•  ").FontColor(accent).Bold();
                                            text.Span(bullet);
                                        });
                                });
                            }
                        }

                        if (doc.Projects.Count > 0)
                        {
                            SectionHeader(main, "Projetos", accent);
                            foreach (var project in doc.Projects)
                            {
                                main.Item().PaddingBottom(7).Column(item =>
                                {
                                    item.Item().Text(project.Name).Bold();
                                    if (!string.IsNullOrWhiteSpace(project.Link))
                                        item.Item().Text(project.Link).FontSize(8).FontColor(accent);
                                    if (!string.IsNullOrWhiteSpace(project.Description))
                                        item.Item().Text(project.Description);
                                    foreach (var bullet in project.Bullets)
                                        item.Item().PaddingLeft(2).Text("•  " + bullet);
                                });
                            }
                        }
                    });

                    row.ConstantItem(1).Background(Line);

                    row.RelativeItem().PaddingLeft(22).Column(side =>
                    {
                        SectionHeader(side, "Contato", accent, 0);
                        foreach (var (label, value) in ContactList(doc))
                            side.Item().PaddingBottom(5).Column(item =>
                            {
                                item.Item().Text(label).FontSize(7.2f).FontColor(Muted).Bold();
                                item.Item().Text(value).FontSize(8.6f);
                            });

                        if (doc.SkillGroups.Count > 0)
                        {
                            SectionHeader(side, "Competências", accent);
                            foreach (var group in doc.SkillGroups)
                            {
                                side.Item().PaddingBottom(6).Column(item =>
                                {
                                    if (!string.IsNullOrWhiteSpace(group.Name))
                                        item.Item().Text(group.Name).FontSize(7.6f).Bold().FontColor(accent);
                                    item.Item().Text(string.Join(" · ", group.Items)).FontSize(8.4f);
                                });
                            }
                        }

                        if (doc.Education.Count > 0)
                        {
                            SectionHeader(side, "Formação", accent);
                            foreach (var edu in doc.Education)
                                side.Item().PaddingBottom(6).Column(item =>
                                {
                                    item.Item().Text($"{edu.Degree} {edu.Field}".Trim()).Bold().FontSize(8.6f);
                                    item.Item().Text(edu.Institution).FontSize(8.4f).FontColor(accent);
                                    item.Item().Text(edu.PeriodLabel).FontSize(7.6f).FontColor(Muted);
                                });
                        }

                        if (doc.Certifications.Count > 0)
                        {
                            SectionHeader(side, "Certificações", accent);
                            foreach (var cert in doc.Certifications)
                                side.Item().PaddingBottom(4).Column(item =>
                                {
                                    item.Item().Text(cert.Name).FontSize(8.4f).Bold();
                                    item.Item().Text($"{cert.Issuer} {cert.Year}".Trim()).FontSize(7.6f).FontColor(Muted);
                                });
                        }

                        if (doc.Languages.Count > 0)
                        {
                            SectionHeader(side, "Idiomas", accent);
                            foreach (var lang in doc.Languages)
                                side.Item().PaddingBottom(3).Text(text =>
                                {
                                    text.Span(lang.Name + " ").Bold().FontSize(8.4f);
                                    text.Span(lang.Level).FontSize(8.2f).FontColor(Muted);
                                });
                        }
                    });
                });
            });
        });
    }).GeneratePdf();

    // ---------------------------------------------------------------- Classic
    private static byte[] BuildClassic(ResumeDocument doc, string accent) => Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(48);
            page.DefaultTextStyle(x => x.FontFamily(SerifFont).FontSize(9.8f).FontColor(Ink).LineHeight(1.42f));

            page.Content().Column(col =>
            {
                col.Item().AlignCenter().Text(doc.Contact.FullName).FontSize(24).FontColor(Ink);
                if (!string.IsNullOrWhiteSpace(doc.Contact.Headline))
                    col.Item().AlignCenter().PaddingTop(1).Text(doc.Contact.Headline).FontSize(11).FontColor(accent);
                var contact = ContactParts(doc);
                if (contact.Count > 0)
                    col.Item().AlignCenter().PaddingTop(5).Text(string.Join("   |   ", contact)).FontSize(8.4f).FontColor(Muted);
                col.Item().PaddingTop(12).BorderBottom(1.4f).BorderColor(accent);

                if (!string.IsNullOrWhiteSpace(doc.Summary))
                {
                    SectionHeader(col, "Resumo", accent, 12);
                    col.Item().Text(doc.Summary);
                }

                if (doc.Experience.Count > 0)
                {
                    SectionHeader(col, "Experiência Profissional", accent);
                    foreach (var exp in doc.Experience)
                    {
                        col.Item().PaddingTop(4).Row(r =>
                        {
                            r.RelativeItem().Text(exp.Role).Bold().FontSize(10.2f);
                            r.ConstantItem(110).AlignRight().Text(exp.PeriodLabel).FontSize(8.4f).FontColor(Muted);
                        });
                        col.Item().Text($"{exp.Company}{(string.IsNullOrWhiteSpace(exp.Location) ? "" : " — " + exp.Location)}").Italic().FontSize(9).FontColor(accent);
                        foreach (var bullet in exp.Bullets)
                            col.Item().PaddingLeft(10).Text("–  " + bullet);
                        col.Item().PaddingBottom(4);
                    }
                }

                if (doc.Education.Count > 0)
                {
                    SectionHeader(col, "Formação", accent);
                    foreach (var edu in doc.Education)
                    {
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Text($"{edu.Degree} {edu.Field}".Trim()).Bold();
                            r.ConstantItem(100).AlignRight().Text(edu.PeriodLabel).FontSize(8.4f).FontColor(Muted);
                        });
                        col.Item().Text(edu.Institution).FontSize(8.8f).FontColor(Muted);
                    }
                }

                if (doc.SkillGroups.Count > 0)
                {
                    SectionHeader(col, "Competências", accent);
                    foreach (var group in doc.SkillGroups)
                        col.Item().Text(text =>
                        {
                            text.Span(group.Name + ": ").Bold();
                            text.Span(string.Join(", ", group.Items));
                        });
                }

                if (doc.Projects.Count > 0)
                {
                    SectionHeader(col, "Projetos", accent);
                    foreach (var project in doc.Projects)
                    {
                        col.Item().Text(project.Name).Bold();
                        if (!string.IsNullOrWhiteSpace(project.Description)) col.Item().Text(project.Description);
                        foreach (var bullet in project.Bullets) col.Item().PaddingLeft(10).Text("–  " + bullet);
                    }
                }

                if (doc.Certifications.Count > 0)
                {
                    SectionHeader(col, "Certificações", accent);
                    foreach (var cert in doc.Certifications)
                        col.Item().Text($"{cert.Name} — {cert.Issuer} {cert.Year}".Trim());
                }

                if (doc.Languages.Count > 0)
                {
                    SectionHeader(col, "Idiomas", accent);
                    col.Item().Text(string.Join("   ·   ", doc.Languages.Select(l => $"{l.Name} ({l.Level})")));
                }
            });
        });
    }).GeneratePdf();

    // ---------------------------------------------------------------- Minimal
    private static byte[] BuildMinimal(ResumeDocument doc, string accent) => Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(46);
            page.DefaultTextStyle(x => x.FontFamily(DefaultFont).FontSize(9.2f).FontColor(Ink).LineHeight(1.45f));

            page.Content().Column(col =>
            {
                col.Item().Text(doc.Contact.FullName).FontSize(28).Light().FontColor(Ink);
                if (!string.IsNullOrWhiteSpace(doc.Contact.Headline))
                    col.Item().PaddingTop(1).Text(doc.Contact.Headline).FontSize(11.5f).FontColor(accent).SemiBold();
                var contact = ContactParts(doc);
                if (contact.Count > 0)
                    col.Item().PaddingTop(4).Text(string.Join("   /   ", contact)).FontSize(8.2f).FontColor(Muted);
                col.Item().PaddingTop(12).BorderBottom(2).BorderColor(accent);

                if (!string.IsNullOrWhiteSpace(doc.Summary))
                {
                    SectionHeader(col, "Sobre", accent, 14);
                    col.Item().Text(doc.Summary);
                }

                if (doc.Experience.Count > 0)
                {
                    SectionHeader(col, "Experiência", accent);
                    foreach (var exp in doc.Experience)
                    {
                        col.Item().PaddingTop(5).Row(r =>
                        {
                            r.RelativeItem().Text(exp.Role).Bold();
                            r.ConstantItem(100).AlignRight().Text(exp.PeriodLabel).FontSize(7.8f).FontColor(Muted);
                        });
                        col.Item().Text($"{exp.Company}{(string.IsNullOrWhiteSpace(exp.Location) ? "" : " · " + exp.Location)}").FontSize(8.4f).FontColor(accent);
                        foreach (var bullet in exp.Bullets)
                            col.Item().PaddingLeft(8).Text("— " + bullet);
                        col.Item().PaddingBottom(5);
                    }
                }

                if (doc.SkillGroups.Count > 0)
                {
                    SectionHeader(col, "Competências", accent);
                    foreach (var group in doc.SkillGroups)
                        col.Item().Text(text =>
                        {
                            text.Span(group.Name.ToUpperInvariant() + "  ").FontSize(7.6f).Bold().FontColor(Muted);
                            text.Span(string.Join("  ·  ", group.Items));
                        });
                }

                if (doc.Education.Count > 0)
                {
                    SectionHeader(col, "Formação", accent);
                    foreach (var edu in doc.Education)
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Text($"{edu.Degree} {edu.Field} — {edu.Institution}".Trim());
                            r.ConstantItem(100).AlignRight().Text(edu.PeriodLabel).FontSize(7.8f).FontColor(Muted);
                        });
                }

                if (doc.Languages.Count > 0)
                {
                    SectionHeader(col, "Idiomas", accent);
                    col.Item().Text(string.Join("   ·   ", doc.Languages.Select(l => $"{l.Name} — {l.Level}")));
                }

                if (doc.Certifications.Count > 0)
                {
                    SectionHeader(col, "Certificações", accent);
                    col.Item().Text(string.Join("   ·   ", doc.Certifications.Select(c => $"{c.Name} ({c.Issuer}, {c.Year})")));
                }
            });
        });
    }).GeneratePdf();

    // ---------------------------------------------------------------- Compact / ATS
    private static byte[] BuildCompact(ResumeDocument doc, string accent) => Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(x => x.FontFamily(DefaultFont).FontSize(9f).FontColor("#000000").LineHeight(1.32f));

            page.Content().Column(col =>
            {
                col.Item().Text(doc.Contact.FullName).FontSize(18).Bold();
                if (!string.IsNullOrWhiteSpace(doc.Contact.Headline))
                    col.Item().Text(doc.Contact.Headline).FontSize(10).SemiBold();
                var contact = ContactParts(doc);
                if (contact.Count > 0) col.Item().Text(string.Join(" | ", contact)).FontSize(8.4f);

                if (!string.IsNullOrWhiteSpace(doc.Summary))
                {
                    SimpleHeader(col, "Resumo profissional");
                    col.Item().Text(doc.Summary);
                }

                if (doc.Experience.Count > 0)
                {
                    SimpleHeader(col, "Experiência profissional");
                    foreach (var exp in doc.Experience)
                    {
                        col.Item().PaddingTop(2).Text(text =>
                        {
                            text.Span(exp.Role).Bold();
                            if (!string.IsNullOrWhiteSpace(exp.PeriodLabel)) text.Span(" | " + exp.PeriodLabel);
                        });
                        col.Item().Text($"{exp.Company}{(string.IsNullOrWhiteSpace(exp.Location) ? "" : " | " + exp.Location)}");
                        foreach (var bullet in exp.Bullets)
                            col.Item().Text("- " + bullet);
                    }
                }

                if (doc.SkillGroups.Count > 0)
                {
                    SimpleHeader(col, "Competências");
                    foreach (var group in doc.SkillGroups)
                        col.Item().Text($"{group.Name}: {string.Join(", ", group.Items)}");
                }

                if (doc.Education.Count > 0)
                {
                    SimpleHeader(col, "Formação acadêmica");
                    foreach (var edu in doc.Education)
                        col.Item().Text($"{edu.Degree} {edu.Field} | {edu.Institution} | {edu.PeriodLabel}".Trim());
                }

                if (doc.Projects.Count > 0)
                {
                    SimpleHeader(col, "Projetos");
                    foreach (var project in doc.Projects)
                    {
                        col.Item().Text($"{project.Name} {project.Link}".Trim()).Bold();
                        if (!string.IsNullOrWhiteSpace(project.Description)) col.Item().Text(project.Description);
                        foreach (var bullet in project.Bullets) col.Item().Text("- " + bullet);
                    }
                }

                if (doc.Certifications.Count > 0)
                {
                    SimpleHeader(col, "Certificações");
                    foreach (var cert in doc.Certifications)
                        col.Item().Text($"{cert.Name} | {cert.Issuer} | {cert.Year}".Trim());
                }

                if (doc.Languages.Count > 0)
                {
                    SimpleHeader(col, "Idiomas");
                    col.Item().Text(string.Join(" | ", doc.Languages.Select(l => $"{l.Name}: {l.Level}")));
                }
            });
        });
    }).GeneratePdf();

    // ---------------------------------------------------------------- helpers
    private static void SectionHeader(ColumnDescriptor col, string title, string accent, float top = 16)
    {
        col.Item().PaddingTop(top).PaddingBottom(4).BorderBottom(1).BorderColor(accent)
            .Text(title.ToUpperInvariant()).FontSize(8.4f).Bold().FontColor(accent);
    }

    private static void SimpleHeader(ColumnDescriptor col, string title)
    {
        col.Item().PaddingTop(9).PaddingBottom(2).Text(title.ToUpperInvariant()).FontSize(9).Bold();
    }

    private static List<string> ContactParts(ResumeDocument doc)
    {
        var list = new List<string>();
        void Add(string? v) { if (!string.IsNullOrWhiteSpace(v)) list.Add(v.Trim()); }
        Add(doc.Contact.Email);
        Add(doc.Contact.Phone);
        Add(doc.Contact.Location);
        Add(doc.Contact.LinkedIn);
        Add(doc.Contact.Website);
        Add(doc.Contact.Github);
        return list;
    }

    private static IEnumerable<(string Label, string Value)> ContactList(ResumeDocument doc)
    {
        if (!string.IsNullOrWhiteSpace(doc.Contact.Email)) yield return ("E-MAIL", doc.Contact.Email);
        if (!string.IsNullOrWhiteSpace(doc.Contact.Phone)) yield return ("TELEFONE", doc.Contact.Phone);
        if (!string.IsNullOrWhiteSpace(doc.Contact.Location)) yield return ("LOCAL", doc.Contact.Location);
        if (!string.IsNullOrWhiteSpace(doc.Contact.LinkedIn)) yield return ("LINKEDIN", doc.Contact.LinkedIn);
        if (!string.IsNullOrWhiteSpace(doc.Contact.Website)) yield return ("SITE", doc.Contact.Website);
        if (!string.IsNullOrWhiteSpace(doc.Contact.Github)) yield return ("GITHUB", doc.Contact.Github);
    }

    private static string SafeColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return "#1F6F5C";
        var value = hex.Trim();
        if (!value.StartsWith('#')) value = "#" + value;
        return System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$") ? value : "#1F6F5C";
    }
}
