# Vita

Aplicativo local para Windows que reúne currículo, análise ATS, adaptação de currículo por vaga, cartas de apresentação, busca de vagas, acompanhamento de candidaturas e uma assistente de IA que propõe mudanças no currículo antes de aplicá-las.

A Vita é um projeto original: a interface, os textos, os modelos de currículo e a identidade visual são próprios. Nenhum conteúdo, marca ou ativo de terceiros é usado.

---

## Abrir a Vita no dia a dia

A versão instalada fica em `%LOCALAPPDATA%\Programs\Vita` e é **autocontida**: não exige instalar o .NET. O instalador cria dois atalhos com o ícone da Vita:

- **Área de Trabalho → Vita**
- **Menu Iniciar → Vita**

Dê dois cliques no atalho. O navegador abre em `http://localhost:5180` e a Vita fica pronta para usar. Se você clicar no atalho de novo, a cópia já aberta é reaproveitada e o navegador apenas volta para o app — não há dois servidores rodando.

Para encerrar, use **Configurações → Sobre a Vita → Encerrar Vita**. Fechar somente a aba do navegador não encerra o aplicativo.

Se a porta `5180` estiver ocupada por outro programa, a Vita escolhe automaticamente a próxima porta livre (`5181`, `5182`…) e abre o navegador no endereço correto.

### Onde ficam seus dados

Cada perfil tem a própria pasta e o próprio banco. O nome digitado na entrada vira o nome da pasta:

| Como foi instalada | Perfis e chaves |
|---|---|
| Instalada (atalho) | `%LOCALAPPDATA%\Programs\Vita\App_Data\perfis\<nome>\` |
| Portátil | `App_Data\perfis\<nome>\` ao lado do `Vita.exe` |
| Desenvolvimento | `src\Vita.App\App_Data\perfis\<nome>\` |

Dentro da pasta do perfil ficam `vita.db` (currículos, candidaturas, cartas e conversas) e `perfil.json`. As chaves de proteção ficam em `App_Data\keys`.

Para backup, copie a pasta `App_Data`. Para atualizar a versão instalada, rode o instalador novamente: ele substitui os arquivos do programa e **preserva** o `App_Data`.

> Se você usava uma versão antiga com um único `vita.db`, a Vita copia as contas reais para `perfis\<nome>\` no primeiro início, remove qualquer conta de demonstração e guarda o banco antigo em `App_Data\backup\`.

### Pacote portátil para compartilhar

`dist\Vita-Portatil.zip` (cerca de 62 MB) contém a mesma versão, pronta para outra pessoa:

- extraia a pasta em um local do usuário (Documentos, Área de Trabalho etc.);
- execute `Vita.exe`;
- o pacote **não inclui** `App_Data` (seus dados), perfis já usados nem chave de API;
- o arquivo `LEIA-ME.txt` dentro do ZIP explica entrada por nome, IA opcional, SmartScreen e backup.

> O executável não tem assinatura digital. No primeiro uso, o SmartScreen pode exibir “O Windows protegeu o seu PC”. Clique em **Mais informações → Executar assim mesmo**.

## Requisitos

**Para usar a versão instalada ou portátil:**

- Windows 10/11, 64 bits
- Nenhuma instalação do .NET necessária

**Para desenvolver ou publicar:**

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Opcional: [Ollama](https://ollama.com) local ou uma chave de um provedor compatível com OpenAI
- Conexão com a internet para buscar vagas (o app continua funcionando offline com dados locais)

## Como executar em desenvolvimento

```powershell
cd E:\Projetonovo\vita
dotnet run --project src\Vita.App\Vita.App.csproj
```

O app abre automaticamente em `http://localhost:5180`.

Para rodar sem abrir o navegador:

```powershell
$env:VITA_NO_BROWSER="1"
dotnet run --project src\Vita.App\Vita.App.csproj
```

Para gerar apenas o build:

```powershell
dotnet build src\Vita.App\Vita.App.csproj
```

> O perfil de inicialização `Vita` já define o ambiente `Development` e a porta `5180`.

## Publicar e distribuir

Os scripts ficam em `tools\` e devem ser executados na pasta `vita`:

```powershell
# 1. Gera publish\win-x64 com o executável autocontido (sem console, ~62 MB)
powershell -ExecutionPolicy Bypass -File tools\publish.ps1

# 2. Instala em %LOCALAPPDATA%\Programs\Vita e cria os atalhos
powershell -ExecutionPolicy Bypass -File tools\install.ps1

# 3. Gera dist\Vita-Portatil.zip limpo, para compartilhar
powershell -ExecutionPolicy Bypass -File tools\make-portable.ps1
```

- `publish.ps1` usa `PublishSingleFile`, compressão e `WinExe` (sem janela de console). Também remove arquivos que só servem para build/IIS.
- `install.ps1` fecha a cópia aberta, copia os arquivos, **preserva o `App_Data`** existente e cria os atalhos da Área de Trabalho e do Menu Iniciar. Ele também instala o `Desinstalar Vita.cmd`.
- `make-portable.ps1` remove `DefaultApiKey` do `appsettings.json`, não copia `App_Data` e inclui o `LEIA-ME.txt`.
- `make-icon.ps1` regenera `src\Vita.App\Assets\vita.ico` (16–256 px) sem ferramentas externas.

Para testar com uma pasta de dados separada, use a variável `Vita__DataDir`:

```powershell
$env:Vita__DataDir="$env:TEMP\vita-teste"
$env:VITA_NO_BROWSER="1"
.\publish\win-x64\Vita.exe
```

## Entrada por nome (perfis locais)

Não há e-mail, senha nem conta na nuvem. Na primeira vez, digite seu nome e a Vita cria um perfil local. Da próxima vez, escolha o nome na lista ou digite-o novamente.

- Cada nome tem uma pasta separada em `App_Data\perfis\<nome>\` com currículos, candidaturas, cartas e conversas próprios.
- Para outra pessoa usar o mesmo computador, basta entrar com outro nome: ela começa com um espaço vazio.
- Em **Configurações → Conta** você vê a pasta do perfil, pode abri-la no Explorer e trocar de perfil.
- O app é feito para um usuário local por vez. Não há senha: quem usa o mesmo Windows já tem acesso aos arquivos.

## Funcionalidades

### Painel (`/`)
Resumo de candidaturas ativas, entrevistas, propostas e taxa de resposta, além de próximos passos, vagas salvas e atalhos para as áreas principais.

### Currículos (`/resumes` e `/resumes/{id}/edit`)
- Criação, duplicação, exclusão e edição de currículos.
- Contato, resumo, experiências, formação, competências, idiomas, projetos e certificações.
- Pré-visualização em tempo real com quatro modelos: `aurora`, `classico`, `minimal` e `compacto`.
- Cor de destaque por currículo.
- Exportação em **PDF** (QuestPDF) e **DOCX** (Open XML).
- Importação de currículo em PDF, DOCX ou TXT, ou colando o texto.
- Extração automática de contato, resumo, experiências, formação e competências, com prévia antes de criar o currículo.
- Salvamento automático com indicador de estado.

### Análise ATS (`/ats`)
- Nota geral com critérios explicados.
- Verificação de estrutura, contato, resumo, bullets, métricas, competências e formatação.
- Comparação com uma descrição de vaga colada pelo usuário.
- Lista de palavras-chave encontradas e ausentes.
- Sugestões acionáveis, sem inventar experiências.

### Adaptação por vaga (`/jobs` → detalhes → adaptar)
- Busca em fontes públicas (**Arbeitnow** e **Remotive**).
- Cálculo local de aderência entre currículo e vaga.
- Filtros por texto, localização e vagas remotas.
- Salvamento de vagas e envio para o acompanhamento.
- Geração de uma versão adaptada do currículo a partir da descrição da vaga.

### Candidaturas (`/applications`)
- Pipeline por status: salva, aplicada, entrevista, proposta, rejeitada e arquivada.
- Notas, próximos passos, data de aplicação e vínculo com currículo.
- Criação, edição e exclusão com confirmação.

### Cartas de apresentação (`/cover-letters`)
- Geração local a partir do currículo, empresa, cargo e descrição da vaga.
- Edição livre, tons e idiomas.
- Exportação em PDF.

### Assistente de IA (`/assistant`)
- Conversa por thread, com histórico salvo.
- Seleção do currículo e de uma vaga alvo como contexto.
- A assistente responde em Markdown e propõe mudanças estruturadas: resumo, título, bullets, competências etc.
- **Nenhuma mudança é aplicada automaticamente.** O usuário revisa o cartão de proposta e clica em **Aplicar no currículo**.
- Quando o provedor de IA falha, a Vita usa um modo local determinístico: ele só reorganiza o que já existe no currículo, avisa que está sem IA e continua exigindo aprovação.

### Modelos (`/templates`)
- Galeria dos quatro modelos, com prévia e criação de currículo a partir do modelo.

### Configurações (`/settings`)
- Provedor de IA opcional, com presets: **Sem IA**, **Ollama local**, **Ollama Cloud** e **Compatível com OpenAI**.
- Assistente de instalação do Ollama local: detecta instalação, inicia o serviço, instala via winget ou site oficial, baixa o modelo recomendado e mostra diagnóstico.
- URL base, modelo, temperatura, modo JSON e chave.
- Teste de conexão.
- Perfil local: pasta do perfil, abrir no Explorer e trocar de perfil.
- Exportação de todos os dados em JSON.
- Preferências de aparência e idioma.
- **Encerrar Vita**, para fechar o aplicativo local sem usar o Gerenciador de Tarefas.

## Configuração de IA (opcional)

A Vita funciona inteiramente sem IA. Currículos, análise ATS, vagas, cartas e a assistente em modo local não dependem de nenhum provedor. Novos perfis começam com **Sem IA**.

Provedores disponíveis:

| Provedor | URL base padrão | Observação |
|---|---|---|
| Sem IA | — | Padrão. Usa apenas recursos locais. |
| Ollama local | `http://localhost:11434` | Grátis; roda na sua máquina. A Vita ajuda a instalar e baixar o modelo. |
| Ollama Cloud | `https://ollama.com` | Precisa da sua chave e de créditos na conta. |
| Compatível com OpenAI | `https://api.openai.com/v1` | OpenAI, Groq, OpenRouter e outros. |

O padrão fica em `src\Vita.App\appsettings.json` no código-fonte e não contém chave:

```json
"Ai": {
  "DefaultProvider": "none",
  "DefaultBaseUrl": "",
  "DefaultModel": "",
  "LocalBaseUrl": "http://localhost:11434"
}
```

A chave informada por você é criptografada com **ASP.NET Core Data Protection** antes de ser gravada no banco do perfil. As chaves de proteção ficam em `App_Data\keys` (na versão instalada, `%LOCALAPPDATA%\Programs\Vita\App_Data\keys`).

### Ollama local pelo próprio app

Em **Configurações → Inteligência artificial → Ollama local**, a Vita:

1. verifica se o Ollama está instalado e se o serviço está rodando;
2. mostra memória RAM e espaço livre, com um modelo recomendado;
3. pode instalar via `winget` (ou abrir o site oficial) e iniciar o serviço;
4. baixa o modelo escolhido (`llama3.1` por padrão);
5. testa a conexão e lista os modelos disponíveis.

Também é possível fazer manualmente:

```powershell
ollama serve
ollama pull llama3.1
```

Depois, em **Configurações → Inteligência artificial**, selecione **Ollama local**, escolha o modelo e salve.

### Ollama Cloud sem créditos

Se a conta do Ollama Cloud estiver sem créditos, o provedor responde:

```
402 Add usage credits to use cloud models
```

A Vita mostra a mensagem de forma clara e mantém os recursos locais funcionando. Para geração com IA de verdade, adicione créditos no Ollama Cloud, use o Ollama local ou outro provedor compatível com OpenAI.

## Funcionamento offline

- **Vagas:** se Arbeitnow ou Remotive falharem, a outra fonte continua; se ambas falharem, o app avisa e mantém as vagas já salvas. O matching é sempre local.
- **ATS, adaptação, cartas e matching:** heurísticas locais, sem dependência de rede.
- **Assistente:** sem provedor disponível, usa `LocalAssistant` e propõe apenas mudanças seguras baseadas no conteúdo existente.
- **Exportação:** PDF e DOCX são gerados localmente.

## Dados e privacidade

- Cada perfil tem um banco SQLite próprio:
  - instalado: `%LOCALAPPDATA%\Programs\Vita\App_Data\perfis\<nome>\vita.db`
  - portátil: `App_Data\perfis\<nome>\vita.db` ao lado do `Vita.exe`
  - desenvolvimento: `src\Vita.App\App_Data\perfis\<nome>\vita.db`
- Chaves de proteção: pasta `keys` dentro do mesmo `App_Data`.
- Nenhum dado é enviado a servidores da Vita. Apenas as chamadas de IA vão para o provedor configurado e as buscas de vagas vão para Arbeitnow/Remotive.
- A chave de API nunca é exibida de volta na interface; o campo mostra apenas um placeholder quando já existe uma chave salva.
- O pacote portátil é gerado sem chave e sem `App_Data`.

### Apagar um perfil ou recomeçar

Pare o app (**Configurações → Encerrar Vita**) e apague a pasta do perfil:

```powershell
# Versão instalada — troque "maria-silva" pelo nome da pasta exibida em Configurações → Conta
Remove-Item "$env:LOCALAPPDATA\Programs\Vita\App_Data\perfis\maria-silva" -Recurse
Start-Process "$env:USERPROFILE\Desktop\Vita.lnk"
```

Na próxima entrada com o mesmo nome, a Vita cria um perfil vazio com um currículo em branco. Para apagar todos os perfis, remova a pasta `perfis` inteira.

## Estrutura do projeto

```
vita/
├─ Directory.Build.props
├─ README.md
├─ tools/                        # Publicação, instalação, ícone e LEIA-ME
│  ├─ publish.ps1
│  ├─ install.ps1
│  ├─ uninstall.ps1
│  ├─ make-portable.ps1
│  ├─ make-icon.ps1
│  └─ LEIA-ME.txt
├─ publish/                      # Gerado por publish.ps1
├─ dist/                         # Gerado por make-portable.ps1
└─ src/
   ├─ Vita.Core/                 # Modelos e lógica sem dependência web
   │  ├─ ResumeDocument.cs
   │  ├─ ResumeScorer.cs         # Análise ATS
   │  ├─ Tailoring.cs            # Matching e adaptação por vaga
   │  ├─ TextAnalysis.cs
   │  ├─ ChatActions.cs          # Parse e aplicação das propostas
   │  ├─ LocalAssistant.cs       # Fallback sem IA
   │  └─ PreviewData.cs          # Placeholders neutros para prévias
   └─ Vita.App/                  # Blazor Web App (Interactive Server)
      ├─ Program.cs              # Auth, endpoints, DI, instância única, portas
      ├─ NativeDialog.cs         # Aviso nativo em erros fatais
      ├─ Assets/vita.ico         # Ícone do executável e dos atalhos
      ├─ Components/
      │  ├─ Pages/               # Painel, currículos, ATS, vagas, etc.
      │  └─ Shared/              # Chat, modal, preview, ícones, toasts
      ├─ Data/                   # DbContext e entidades
      ├─ Services/               # IA, stores, PDF, DOCX, importação, vagas
      └─ wwwroot/                # CSS, JavaScript e favicon
```

## Stack

- .NET 10 / ASP.NET Core Blazor Web App (Interactive Server)
- EF Core 10 + SQLite
- QuestPDF 2025.7.1
- DocumentFormat.OpenXml 3.5.1
- PdfPig 0.1.16
- Cookie authentication (`VitaCookie`) com perfis por nome
- Data Protection para criptografar a chave de IA

## Limitações conhecidas

- A IA é opcional e depende de um provedor configurado. Sem créditos ou sem Ollama, a Vita avisa e mantém os recursos locais.
- O Ollama Cloud pode responder HTTP 402 quando a conta está sem créditos; use o Ollama local ou outro provedor.
- A busca de vagas depende das APIs públicas de Arbeitnow e Remotive e pode variar em quantidade e qualidade.
- A importação de PDF funciona melhor com PDFs de texto; PDFs escaneados (imagem) não têm OCR.
- As sugestões de IA e do modo local são propostas: sempre revise antes de aplicar.
- O executável é local e **não assinado digitalmente**; por isso o SmartScreen pode pedir confirmação no primeiro uso.
- Os perfis são locais e não têm senha: quem usa a mesma conta do Windows consegue abrir a pasta de dados.
