namespace Vita.Core;

// Neutral placeholders used only to illustrate how a template looks.
// They are not a person, a resume, or sample data from any account.
public static class PreviewData
{
    public static ResumeDocument TemplatePreview() => new()
    {
        Contact = new ContactInfo
        {
            FullName = "Seu nome",
            Headline = "Cargo ou área de atuação",
            Email = "voce@email.com",
            Phone = "(00) 00000-0000",
            Location = "Cidade, país",
            LinkedIn = "linkedin.com/in/seuperfil",
            Website = "seusite.com"
        },
        Summary = "Resumo profissional: em duas ou três linhas, explique o que você faz, sua especialidade e um resultado concreto que já entregou.",
        Experience = new List<ExperienceItem>
        {
            new()
            {
                Role = "Cargo atual",
                Company = "Empresa",
                Location = "Cidade (modelo de trabalho)",
                StartDate = "2022-01",
                Current = true,
                Bullets = new List<string>
                {
                    "Descreva uma entrega começando com um verbo de ação, o contexto e o resultado mensurável.",
                    "Mostre o impacto do seu trabalho com números, prazos ou mudanças concretas.",
                    "Cite ferramentas, métodos ou times com quem você colaborou."
                }
            },
            new()
            {
                Role = "Cargo anterior",
                Company = "Empresa",
                Location = "Cidade",
                StartDate = "2019-01",
                EndDate = "2021-12",
                Bullets = new List<string>
                {
                    "Explique o escopo da função e a principal responsabilidade.",
                    "Inclua uma conquista com resultado verificável."
                }
            }
        },
        Education = new List<EducationItem>
        {
            new() { Institution = "Instituição de ensino", Degree = "Grau", Field = "Curso", StartDate = "2014-02", EndDate = "2018-12" }
        },
        SkillGroups = new List<SkillGroup>
        {
            new() { Name = "Competências técnicas", Items = new() { "Ferramenta", "Método", "Tecnologia" } },
            new() { Name = "Competências comportamentais", Items = new() { "Comunicação", "Colaboração", "Organização" } }
        },
        Languages = new List<LanguageItem>
        {
            new() { Name = "Português", Level = "Nativo" },
            new() { Name = "Inglês", Level = "Nível" }
        }
    };
}
