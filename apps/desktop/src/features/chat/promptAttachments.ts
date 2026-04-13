export type PromptAttachmentKind = 'file' | 'image';

export interface PromptAttachment {
  kind: PromptAttachmentKind;
  path: string;
  name: string;
}

const ATTACHMENT_MARKER_PATTERN = /<clawsharp-attachment>(?<json>.*?)<\/clawsharp-attachment>/gs;

export function buildPromptWithAttachments(prompt: string, attachments: PromptAttachment[]) {
  const trimmedPrompt = prompt.trim();
  if (attachments.length === 0) {
    return trimmedPrompt;
  }

  const markers = attachments
    .map((attachment) => `<clawsharp-attachment>${JSON.stringify(attachment)}</clawsharp-attachment>`)
    .join('\n');

  return trimmedPrompt.length > 0
    ? `${trimmedPrompt}\n\n${markers}`
    : markers;
}

export function parsePromptAttachments(rawPrompt: string) {
  const attachments: PromptAttachment[] = [];

  for (const match of rawPrompt.matchAll(ATTACHMENT_MARKER_PATTERN)) {
    const serialized = match.groups?.json;
    if (!serialized) {
      continue;
    }

    try {
      const parsed = JSON.parse(serialized) as Partial<PromptAttachment>;
      if (!isPromptAttachment(parsed)) {
        continue;
      }

      attachments.push(parsed);
    } catch {
      continue;
    }
  }

  const prompt = rawPrompt
    .replace(ATTACHMENT_MARKER_PATTERN, '')
    .replace(/\n{3,}/g, '\n\n')
    .trim();

  return {
    prompt,
    attachments,
  };
}

function isPromptAttachment(value: Partial<PromptAttachment>): value is PromptAttachment {
  return (value.kind === 'file' || value.kind === 'image')
    && typeof value.path === 'string'
    && value.path.trim().length > 0
    && typeof value.name === 'string'
    && value.name.trim().length > 0;
}
