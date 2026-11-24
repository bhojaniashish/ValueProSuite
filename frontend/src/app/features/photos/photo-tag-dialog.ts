import { Component, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Dialog } from '../../core/ui/dialog';
import { PHOTO_TAGS, PhotoTag } from './photos.api';

/** "What does this photo show?" asked after taking or choosing photos. */
@Component({
  selector: 'vp-photo-tag-dialog',
  imports: [Dialog, FormsModule],
  template: `
    <vp-dialog [title]="count() === 1 ? 'What does this photo show?' : 'What do these ' + count() + ' photos show?'" icon="fa-camera" (closed)="closed.emit()">
      @if (note()) { <p class="small muted note">{{ note() }}</p> }
      <div class="tag-pick" role="radiogroup" aria-label="What the photo shows">
        @for (x of tags; track x.value) {
          <button type="button" class="tp" role="radio" [class.on]="tag() === x.value" [attr.aria-checked]="tag() === x.value" (click)="tag.set(x.value)">
            <i class="fa-solid" [class]="x.icon"></i> {{ x.label }}
          </button>
        }
      </div>
      <div class="fld gap">
        <label for="pt-cap">Caption <span class="hint">(optional)</span></label>
        <input id="pt-cap" [ngModel]="caption()" (ngModelChange)="caption.set($event)" placeholder="Living room, crack on east wall…" />
      </div>
      <ng-container footer>
        <button class="btn btn-ghost" type="button" (click)="closed.emit()">Cancel</button>
        <button class="btn btn-pri" type="button" (click)="done.emit({ tag: tag(), caption: caption().trim() || null })">
          <i class="fa-solid fa-check"></i> {{ count() === 1 ? 'Add photo' : 'Add ' + count() + ' photos' }}
        </button>
      </ng-container>
    </vp-dialog>
  `,
  styles: `
    .note { margin: 0 0 12px; }
    .tag-pick { display: flex; flex-wrap: wrap; gap: 8px; }
    .tp { display: inline-flex; align-items: center; gap: 6px; min-height: var(--control-h); padding: 6px 12px; border: 1px solid var(--line);
      border-radius: 99px; font-size: 13px; color: var(--ink-2); }
    .tp i { font-size: 12px; color: var(--muted); }
    .tp.on { border-color: var(--brand); background: var(--brand-soft); color: var(--brand); font-weight: 600; }
    .tp.on i { color: var(--brand); }
    .gap { margin-top: 14px; }
  `,
})
export class PhotoTagDialog implements OnInit {
  readonly count = input(1);
  readonly initialTag = input<PhotoTag>('Other');
  readonly note = input<string | null>(null);
  readonly done = output<{ tag: PhotoTag; caption: string | null }>();
  readonly closed = output<void>();

  protected readonly tags = PHOTO_TAGS;
  protected readonly tag = signal<PhotoTag>('Other');
  protected readonly caption = signal('');

  ngOnInit(): void {
    this.tag.set(this.initialTag());
  }
}
