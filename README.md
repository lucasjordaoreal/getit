# GetIt

**GetIt** é um gerenciador de downloads moderno e de alta performance para Windows, construído com **WinUI 3** e **.NET 10**. Ele foi projetado para oferecer uma experiência fluida, visualmente impressionante e extremamente funcional para baixar vídeos e áudios de diversas plataformas.
## ✨ Funcionalidades Principais

### 📥 Downloads Multiformato
- **Vídeo:** Baixe vídeos em múltiplas resoluções (4K, 1080p, 720p, etc.) com detecção automática de formatos.
- **Áudio:** Converta vídeos diretamente para áudio de alta qualidade (320kbps, 256kbps, 192kbps).
- **Suporte Amplo:** Compatível com YouTube, Instagram, TikTok, X (Twitter) e centenas de outros sites (via engine yt-dlp).

### 🔄 Sistema de Fila Inteligente (Modo Fila)
- **Downloads em Lote:** Alterne para o "Modo Fila" para adicionar múltiplos links simultaneamente.
- **Configurações Individuais:** Cada item na fila pode ter sua própria configuração de qualidade e formato independente dos outros.
- **Drag & Drop:** Reorganize a ordem dos downloads simplesmente arrastando e soltando os cards na interface.
- **Processamento Sequencial:** Baixe toda a lista com um único clique.

### ⚡ Experiência do Usuário (UX)
- **Modo Link Único:** Interface simplificada para quem deseja baixar apenas um vídeo por vez de forma rápida.
- **Design Liquid Glass:** Interface com efeitos de nebulosa, transparências modernas e animações fluidas de 60 FPS.
- **Portátil:** Gerado como um único arquivo `.exe`, sem necessidade de instalação ou dependências externas.

---

## 🛠️ Como Usar
1. **Escolha o Modo:** Selecione entre "Link Único" ou "Modo Fila" no interruptor superior.
2. **Cole o Link:** Copie a URL do vídeo desejado e cole no campo principal.
3. **Configure:** Escolha se deseja Vídeo ou Áudio e selecione a qualidade desejada no card.
4. **Baixe:** Clique em "Baixar" ou "Baixar Toda a Fila". O app solicitará a pasta de destino apenas uma vez por lote.

---

## 🚀 Tecnologias Utilizadas

- **Interface:** WinUI 3 (Windows App SDK)
- **Lógica:** C# 13 / .NET 10
- **Engine de Download:** yt-dlp (Integrado)
- **Estilização:** Vanilla CSS & XAML Composition Projections

---

Desenvolvido para ser o downloader mais rápido e bonito do Windows.
