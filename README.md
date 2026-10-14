# GetIt

GetIt é um aplicativo Windows para download de mídia, com foco em confiabilidade, boa experiência de uso e estrutura de manutenção profissional. O produto combina interface WinUI 3, downloads via yt-dlp e gestão de fila, histórico e configurações em uma solução que foi preparada para evolução contínua e distribuição.

## Visão do produto

- Download de vídeos e áudios com qualidade configurável
- Suporte a múltiplos links em fila
- Histórico local de downloads concluídos
- Configuração de destino, tema e comportamento da aplicação
- Atualização e manutenção mais fáceis por meio de separação de responsabilidades

## Requisitos

- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 ou Rider
- Windows App SDK / WinUI 3 habilitados para o projeto
- Engine yt-dlp disponível em `Engine/Bin` ou em ambiente de execução compatível

## Estrutura do repositório

```text
GetIt/
├── .github/
│   └── workflows/
│       └── ci.yml
├── docs/
│   └── PROJECT_STRUCTURE.md
├── src/
│   ├── GetIt.App/
│   ├── GetIt.Core/
│   └── ...
├── tests/
│   └── GetIt.Tests/
├── Engine/
├── installer/
├── .gitignore
├── Directory.Build.props
├── GetIt.sln
├── README.md
├── RELEASE.MD
└── ...
```

## Configuração de desenvolvimento

1. Clone o repositório.
2. Abra a solução `GetIt.sln` no Visual Studio ou Rider.
3. Restore as dependências com `dotnet restore GetIt.sln -p:Platform=x64`.
4. Se necessário, valide a engine externa em `Engine/Bin`.

## Compilação

```powershell
dotnet restore GetIt.sln -p:Platform=x64
dotnet build GetIt.sln -p:Platform=x64 --configuration Release
```

## Testes

```powershell
dotnet test GetIt.sln -p:Platform=x64 --configuration Release --logger "console;verbosity=minimal"
```

## Publicação e distribuição

A solução está preparada para builds reproducíveis com suporte a publicação Windows e geração de artefatos para distribuição. O pipeline de CI em `.github/workflows/ci.yml` realiza:

- restore
- build
- testes
- validação de formatação estática

Para empacotamento e distribuição, a pasta `installer/` contém scripts de preparação do instalador e artefatos complementares.

## Arquitetura e manutenção

- `GetIt.App`: interface, interação do usuário, telas e fluxo principal
- `GetIt.Core`: lógica compartilhada, configurações e serviços de infraestrutura
- `GetIt.Tests`: testes automatizados para os fluxos críticos
- `Engine`: binários e dependências externas do runtime de download
- `docs`: documentação operativa e de estrutura do produto

## Observações importantes

- O projeto foi estruturado para manter comportamento funcional existente enquanto melhora organização, consistência e manutenção.
- Não há reescrita de negócio ou introdução de camadas artificiais; os ajustes foram feitos para deixar o repositório profissional e evolutivo sem afetar as funcionalidades do cliente.
- O build e os testes devem ser executados em ambiente Windows, com plataforma x64, para manter compatibilidade com WinUI 3.
