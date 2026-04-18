export interface MobileProject {
  id: string;
  name: string;
  path: string;
  gitBranch?: string | null;
  threadCount: number;
  lastUpdatedAt: string;
}

export interface MobileThread {
  id: string;
  projectId: string;
  title: string;
  summary: string;
  status: string;
  provider: string;
  model: string;
  messageCount: number;
  lastUpdatedAt: string;
}

export interface MobileMessage {
  id: string;
  threadId: string;
  role: string;
  content: string;
  timestamp: string;
}

export interface MobileThreadDetail {
  thread: MobileThread;
  messages: MobileMessage[];
  hasMoreMessages: boolean;
  nextBeforeMessageId?: string | null;
}

export interface MobileApproval {
  id: string;
  threadId: string;
  action: string;
  decision: string;
  createdAt: string;
}

export interface MobileChangedFile {
  path: string;
  status: string;
  additions: number;
  deletions: number;
  threadId: string;
}

export interface MobileDiffLine {
  type: string;
  content: string;
  oldLineNumber?: number | null;
  newLineNumber?: number | null;
}

export interface MobileDiffHunk {
  header: string;
  lines: MobileDiffLine[];
}

export interface MobileDiff {
  filePath: string;
  hunks: MobileDiffHunk[];
}

export interface MobileRunEvent {
  runId: string;
  threadId: string;
  kind: string;
  timestamp: string;
  message?: string | null;
  textDelta?: string | null;
  detail?: string | null;
}
