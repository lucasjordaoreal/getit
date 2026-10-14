# Estrutura do projeto GetIt

A estrutura atual foi organizada para separar responsabilidades centrais do produto, preservando o código-fonte e os artefatos de distribuição em lugares claros e previsíveis.

```text
GetIt/
├── .github/
│   └── workflows/
│       └── ci.yml
├── docs/
│   └── PROJECT_STRUCTURE.md
├── src/
│   ├── GetIt.App/
│   │   ├── App.xaml
│   │   ├── App.xaml.cs
│   │   ├── AppInfo.cs
│   │   ├── AppLogger.cs
│   │   ├── Assets/
│   │   ├── GetIt.App.csproj
│   │   ├── HistoryPage.xaml
│   │   ├── HistoryPage.xaml.cs
│   │   ├── MainPage.xaml
│   │   ├── MainPage.xaml.cs
│   │   ├── MainWindow.xaml
│   │   ├── MainWindow.xaml.cs
│   │   ├── Models/
│   │   ├── Package.appxmanifest
│   │   ├── PostPublishCleanup.ps1
│   │   ├── Properties/
│   │   ├── Services/
│   │   ├── SettingsPage.xaml
│   │   ├── SettingsPage.xaml.cs
│   │   ├── StartupInitializer.cs
│   │   ├── ViewModels/
│   │   ├── app.manifest
│   │   ├── icon.ico
│   │   └── ...
│   ├── GetIt.Core/
│   │   ├── AppInfo.cs
│   │   ├── AppLogger.cs
│   │   ├── GetIt.Core.csproj
│   │   └── Services/
│   └── ...
├── tests/
│   └── GetIt.Tests/
│       ├── AppInfoTests.cs
│       ├── GetIt.Tests.csproj
│       ├── HistoryServiceTests.cs
│       └── SettingsServiceTests.cs
├── tools/
│   └── Engine/
│       └── Bin/
├── deploy/
│   └── installer/
│       └── build_installer.ps1
├── .gitignore
├── Directory.Build.props
├── GetIt.sln
├── README.md
├── RELEASE.MD
└── ...
```

A organização segue a convenção:

- `src/` mantém a aplicação e os componentes compartilhados.
- `tests/` contém a base de testes automatizados.
- `tools/` concentra motores e utilitários externos.
- `deploy/` guarda infraestrutura de distribuição e instaladores.
- `docs/` registra a documentação orientada a manutenção e operação.
- `root` preserva as configurações da solução, o README e os arquivos de governança do produto.
