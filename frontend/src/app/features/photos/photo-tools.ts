export interface PreparedPhoto { image: Blob; thumb: Blob; width: number; height: number }

/** Printed in a strip below a camera photo, as banks expect from geo-tagged site photos. */
export interface PhotoStamp {
  takenAt: Date;
  latitude: number | null;
  longitude: number | null;
  accuracyMetres?: number | null;
  /** Case number and firm, e.g. "VAL/2026/0001 · Om Engineers". */
  line: string;
}

const MAX_SIDE = 1600;
const THUMB_SIDE = 360;

/**
 * Phone photos are 3–8 MB; banks' reports need far less. Resizing to 1600 px (JPEG, ~300 KB) keeps uploads quick on
 * mobile data. EXIF orientation is applied, so photos are upright. With a stamp, the date, time and location are drawn
 * in a strip added below the photo, so nothing in the picture is covered.
 */
export async function preparePhoto(file: Blob, stamp?: PhotoStamp): Promise<PreparedPhoto> {
  const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
  try {
    const [image, size] = await draw(bitmap, MAX_SIDE, 0.82, stamp);
    const [thumb] = await draw(bitmap, THUMB_SIDE, 0.7);
    return { image, thumb, width: size.w, height: size.h };
  } finally {
    bitmap.close();
  }
}

async function draw(src: ImageBitmap, maxSide: number, quality: number, stamp?: PhotoStamp): Promise<[Blob, { w: number; h: number }]> {
  const scale = Math.min(1, maxSide / Math.max(src.width, src.height));
  const w = Math.round(src.width * scale);
  const h = Math.round(src.height * scale);
  const lines = stamp ? stampLines(stamp) : [];
  const font = Math.max(14, Math.round(w / 55));
  const strip = lines.length ? Math.round(font * 1.45 * lines.length + font * 0.9) : 0;

  const canvas = document.createElement('canvas');
  canvas.width = w;
  canvas.height = h + strip;
  const g = canvas.getContext('2d')!;
  g.drawImage(src, 0, 0, w, h);
  if (strip) {
    g.fillStyle = '#111827';
    g.fillRect(0, h, w, strip);
    g.fillStyle = '#ffffff';
    g.textBaseline = 'top';
    lines.forEach((text, i) => {
      g.font = `${i === 0 ? 600 : 400} ${font}px system-ui, -apple-system, Segoe UI, Roboto, sans-serif`;
      g.fillText(text, Math.round(font * 0.7), h + Math.round(font * 0.45 + i * font * 1.45), w - font * 1.4);
    });
  }
  const blob = await new Promise<Blob | null>((res) => canvas.toBlob(res, 'image/jpeg', quality));
  if (!blob) throw new Error('Could not process the photo.');
  return [blob, { w, h: h + strip }];
}

export function stampLines(s: PhotoStamp): string[] {
  const when = s.takenAt.toLocaleString('en-IN', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit', hour12: true });
  const where = s.latitude != null && s.longitude != null
    ? `${s.latitude.toFixed(6)}, ${s.longitude.toFixed(6)}${s.accuracyMetres != null ? ` (±${s.accuracyMetres} m)` : ''}`
    : 'Location not available';
  return [`${when}  ·  ${where}`, s.line];
}

export function newKey(): string {
  return typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
        const r = (Math.random() * 16) | 0;
        return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
      });
}
