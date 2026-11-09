# Application macOS

Le logo source est `src/AgentYattus.Desktop/Assets/Branding/yattus.png`.
Les fichiers `.icns` (Finder/Dock) et `.ico` (fenêtres/Windows) en sont les déclinaisons aux tailles système.

Sur macOS, chaque compilation du projet Desktop crée automatiquement :

```
artifacts/macos/Debug/Agent Yattus.app
```

Lancer ce paquet avec le Finder plutôt que le binaire brut ou `dotnet run` pour que macOS affiche l'identité et l'icône de l'application.
On peut copier le paquet dans Applications. La version de développement conserve le serveur configuré (par défaut localhost:5080).

Une publication Release remplit le paquet `artifacts/macos/Release/Agent Yattus.app` avec les fichiers publiés :

```
dotnet publish src/AgentYattus.Desktop -c Release -r osx-arm64 --self-contained true
```

Avant une distribution publique, signer et notariser le paquet avec un compte Apple Developer.
