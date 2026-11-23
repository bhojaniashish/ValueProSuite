/** "2 min ago", "3 hr ago", "Yesterday", "12 Nov 2026". */
export function timeAgo(value: string | null | undefined, now = new Date()): string {
  if (!value) return 'Never';
  const date = new Date(value);
  const seconds = Math.max(0, (now.getTime() - date.getTime()) / 1000);
  if (seconds < 60) return 'Just now';
  if (seconds < 3600) return `${Math.floor(seconds / 60)} min ago`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)} hr ago`;
  if (seconds < 172800) return 'Yesterday';
  if (seconds < 7 * 86400) return `${Math.floor(seconds / 86400)} days ago`;
  return date.toLocaleDateString('en-IN', { day: 'numeric', month: 'short', year: 'numeric' });
}

export function initials(name: string | null | undefined): string {
  return (name ?? '?')
    .split(/\s+/)
    .filter((w) => /^[\p{L}\p{N}]/u.test(w)) // skip "-", "&", "–"
    .map((p) => p[0])
    .slice(0, 2)
    .join('')
    .toUpperCase();
}

const AVATAR_COLORS = ['#2B50C4', '#7A5AD6', '#1B9AA3', '#C2922E', '#1F9C6B', '#D64550', '#2F77D0', '#D9912B'];

/** Same name, same colour, every time. */
export function avatarColor(name: string): string {
  let hash = 0;
  for (const ch of name) hash = (hash * 31 + ch.charCodeAt(0)) | 0;
  return AVATAR_COLORS[Math.abs(hash) % AVATAR_COLORS.length];
}
