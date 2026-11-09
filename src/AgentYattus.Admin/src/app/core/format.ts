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

/** Un montant en dollars : 4,82 $ ; sous un cent, assez de décimales pour ne pas afficher 0. */
export function usd(amount: number | null | undefined): string {
  if (!amount) return '0 $';
  const digits = amount < 0.01 ? 4 : 2;
  return `${amount.toLocaleString('fr-FR', { minimumFractionDigits: digits, maximumFractionDigits: digits })} $`;
}

/** 1 234 567 → « 1,2M » ; 12 345 → « 12,3k ». */
export function tokens(n: number | null | undefined): string {
  if (!n) return '0';
  if (n >= 1_000_000) return `${(n / 1_000_000).toLocaleString('fr-FR', { maximumFractionDigits: 1 })}M`;
  if (n >= 1_000) return `${(n / 1_000).toLocaleString('fr-FR', { maximumFractionDigits: 1 })}k`;
  return String(n);
}

/** Date courte, en français, à l'heure locale. */
export function when(iso: string): string {
  return new Date(iso).toLocaleString('fr-FR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
}

/** Le type d'une mission, en mots : Question, Revue, Développer (ADR 0016). */
export function kindLabel(kind: string): string {
  return kind === 'Recruitment' ? 'Recrutement RH' : kind === 'Review' ? 'Revue' : kind === 'Develop' ? 'Développer' : 'Question';
}
