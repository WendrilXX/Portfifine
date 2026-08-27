# Portfifine

Ferramentas para migrar recursos compatíveis do Elgato Stream Deck para o
Fifine Control Deck / StreamDock e manter plugins personalizados juntos em um
único repositório.

Autor do projeto: **WendrilXX**.

## Conteúdo

- `StreamDeckPortFifine.bat`: copia plugins e pacotes de ícones compatíveis
  do Stream Deck, instala os plugins incluídos neste repositório, limpa o
  cache e reinicia o Fifine.
- `plugins/com.wendril.spotify.sdPlugin`: plugin Spotify personalizado,
  pronto para uso no Fifine.

## Uso

1. Baixe ou clone este repositório.
2. Feche o Fifine Control Deck, se estiver aberto.
3. Execute `StreamDeckPortFifine.bat` como administrador.
4. Abra o Fifine e procure as ações na categoria correspondente.

O script procura instalações padrão em `%APPDATA%\HotSpot\StreamDock` e, se
existirem, copia plugins e icon packs de `%APPDATA%\Elgato\StreamDeck`.

## Plugin Spotify

O plugin em `plugins/com.wendril.spotify.sdPlugin` é autocontido: suas
dependências, incluindo a DLL necessária, estão incluídas em
`plugin/node_modules`. Não apague essa pasta.

Recursos:

- Play / Pause;
- próxima e faixa anterior;
- volume do aplicativo Spotify em passos de 5%;
- Now Playing com artista, título e capa do álbum.

Ele controla o aplicativo Spotify para Windows localmente através de Windows
SMTC e Core Audio. Não usa OAuth, API Web ou Spotify Premium. O Spotify deve
estar aberto para que as ações funcionem.

## Compatibilidade

- Windows 10 ou superior, x64;
- Fifine Control Deck / StreamDock com Node.js 20 embutido;
- Spotify para Windows.

Plugins oficiais recentes do Elgato que tenham `manifest.json` criptografado
não são compatíveis com o Fifine e não podem ser migrados simplesmente por
cópia. O plugin Spotify deste repositório foi construído especificamente para
o formato de plugins do Fifine/Mirabox.
