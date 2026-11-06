/** « Studio Yattus & Cie » → « studio-yattus-cie » : l'identifiant proposé à partir du nom (le serveur le revérifie). */
export function slugify(name: string): string {
  return name
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 40)
    .replace(/-+$/, '');
}
