/** « 200k », « 1M » : la taille du contexte d'un modèle. */
export function context(tokens: number | null): string {
  if (!tokens) return '—';
  return tokens >= 1_000_000 ? `${+(tokens / 1_000_000).toFixed(1)}M` : `${Math.round(tokens / 1000)}k`;
}

/** Prix par million de tokens, entrée et sortie ; « gratuit » quand les deux sont nuls. */
export function price(input: number | null, output: number | null): string {
  if (input === null || output === null) return '—';
  if (input === 0 && output === 0) return 'gratuit';
  const fmt = (n: number) => `${n.toLocaleString('fr-FR', { maximumFractionDigits: 2 })} $`;
  return `${fmt(input)} · ${fmt(output)}`;
}

/** Date courte, en français, à l'heure locale. */
export function when(iso: string): string {
  return new Date(iso).toLocaleString('fr-FR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
}
