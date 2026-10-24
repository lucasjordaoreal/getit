# 🚀 GetIt - v0.1.0

### ✨ Melhorias:
- **Atualizações:** Deixe seu app sempre atualizado com as atualizações automáticas!
- **Sobre:** Agora o App mostra a versão do aplicativo

# 🚀 GetIt - v0.0.9

### ✨ Melhorias:
- **Histórico de downloads:** Persistência do histórico em arquivo local, com inclusão de novos itens, limite de 100 registros e opção para limpar.
- **Configurações do aplicativo:** Preferências de pasta de download, tema e colagem automática salvas entre sessões; detecção do tema do Windows.
- **Logs e diagnóstico:** Registro de exceções em arquivo local para facilitar a investigação de problemas.
- **Estrutura e qualidade:** Funcionalidades compartilhadas de metadados e serviços centralizadas no projeto `GetIt.Core`, com testes automatizados e validação de build, testes e formatação no CI.

## GetIt - v0.0.8

### ✨ Melhorias:
- **Fila de downloads:** Links agora são adicionados por uma ação explícita, validados antes da análise e podem ser preparados enquanto os metadados são carregados. O início da fila aguarda a conclusão das análises pendentes.
- **Navegação adaptativa:** Corrigido o layout do painel ao recolher, evitando que o cabeçalho e o rodapé ultrapassem a área compacta.
- **Interface consistente:** Cores e contraste foram unificados entre os temas claro e escuro; foram adicionados estados vazios para a fila, o histórico e o player.
- **Feedback e segurança:** A limpeza do histórico pede confirmação, e a atualização do yt-dlp exibe progresso e resultado.
- **Versão:** Metadados do aplicativo, pacote MSIX e instalador atualizados para 0.0.8.

## GetIt - v0.0.7

### 🐛 Correções:
- **YouTube Bot Block:** Correção crítica de um erro onde o YouTube bloqueava os downloads solicitando "Confirmação de que você não é um robô" (Login Required). O tráfego da engine foi forçado para IPv4, contornando o bloqueio de forma transparente sem exigir login ou cookies do navegador.
- **Melhoria no Tratamento de Erros:** O app agora exibe a mensagem de erro real vinda da engine (yt-dlp) em vez de uma mensagem genérica, facilitando diagnósticos futuros.

*Desenvolvido por Lucas Jordão*
