# Galerie de portraits

Les personnages du bureau (ADR 0012) : pour chacun, une pose « travaille » (`<slug>.webp`) et une pose « parle » (`<slug>-parle.webp`), 560 × 560, fond transparent.

- **Provenance** : images générées avec Codex (génération d'images d'OpenAI) pour Agent Yattus, à partir de descriptions originales ; ce ne sont pas des photos de personnes réelles. Détourage nettoyé et conversion en WebP par `scripts/prepare-portraits.py`.
- **Import** : au démarrage, l'API importe ce dossier si la galerie est vide (`Team:SeedFolder` dans `appsettings.json`). `portraits.json` donne les prénoms suggérés, le chef d'équipe, le portrait de « Vous » et l'équipe de départ.
- **Ensuite**, tout se gère dans l'espace admin (Configuration › Portraits et Administration › Équipes) : ce dossier ne sert qu'au premier démarrage.
