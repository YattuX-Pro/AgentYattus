/** Les outils qu'un agent peut recevoir (WorkspaceTools côté serveur), dits simplement. L'écriture garde l'accord humain. */
export const TOOLS = [
  { name: 'list_files', label: 'Parcourir les dossiers', icon: 'lucideFolderSearch' },
  { name: 'read_file', label: 'Lire les fichiers', icon: 'lucideFileText' },
  { name: 'write_file', label: 'Écrire, avec votre accord', icon: 'lucidePenLine' },
] as const;

export interface AgentForm { name: string; summary: string; instructions: string; tools: string[] }

/** Les mêmes règles que le serveur, pour griser « Créer » avant l'aller-retour. */
export function formErrors(form: AgentForm, withInstructions: boolean): string[] {
  const errors: string[] = [];
  const name = form.name.trim();
  if (name.length < 2 || name.length > 40 || !/^[\p{L}\p{N}' -]+$/u.test(name)) errors.push('name');
  if (form.summary.trim().length < 5 || form.summary.trim().length > 200) errors.push('summary');
  if (form.tools.length === 0) errors.push('tools');
  if (withInstructions && (form.instructions.trim().length < 20 || form.instructions.length > 8000)) errors.push('instructions');
  return errors;
}
