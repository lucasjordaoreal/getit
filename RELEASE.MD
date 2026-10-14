# 🚀 GetIt - v0.0.8

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
