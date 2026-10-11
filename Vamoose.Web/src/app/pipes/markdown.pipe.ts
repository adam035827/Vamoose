import { Pipe, PipeTransform } from '@angular/core';
import { marked } from 'marked';

// Output is bound via [innerHTML], so Angular's sanitizer strips any unsafe markup.
@Pipe({ name: 'markdown' })
export class MarkdownPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    if (!value) return '';
    return marked.parse(value, { async: false, gfm: true, breaks: true });
  }
}
