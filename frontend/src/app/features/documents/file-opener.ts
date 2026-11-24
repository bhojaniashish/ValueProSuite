import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { ToastService } from '../../core/ui/toast';
import { DocumentFile, DocumentsApi } from './documents.api';

/**
 * Opens a document file in a new tab (the browser's PDF / image viewer). Files need the sign-in token, so they are
 * fetched first; the tab is opened straight away so pop-up blockers allow it, then pointed at the file.
 */
@Injectable({ providedIn: 'root' })
export class FileOpener {
  private readonly api = inject(DocumentsApi);
  private readonly toast = inject(ToastService);

  async open(f: DocumentFile): Promise<void> {
    const tab = window.open('', '_blank');
    tab?.document.write('<p style="font:14px system-ui;padding:24px;color:#555">Opening…</p>');
    try {
      const blob = await firstValueFrom(this.api.file(f.id));
      const url = URL.createObjectURL(new Blob([blob], { type: f.contentType }));
      if (tab) tab.location.href = url;
      else Object.assign(document.createElement('a'), { href: url, download: f.fileName }).click();
      setTimeout(() => URL.revokeObjectURL(url), 60000);
    } catch (e) {
      tab?.close();
      this.toast.error('Could not open the file', toProblem(e).message);
    }
  }
}
