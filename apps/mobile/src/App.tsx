import { FormEvent, useEffect, useRef, useState } from 'react';
import { MobileApiClient } from './lib/api';
import type {
  MobileApproval,
  MobileChangedFile,
  MobileDiff,
  MobileMessage,
  MobileProject,
  MobileRunEvent,
  MobileThread,
} from './lib/types';

const savedBaseUrl = typeof window === 'undefined'
  ? 'http://127.0.0.1:5055'
  : window.localStorage.getItem('mobile.api.baseUrl') || 'http://127.0.0.1:5055';
const savedToken = typeof window === 'undefined'
  ? ''
  : window.localStorage.getItem('mobile.api.token') || '';

export default function App() {
  const [baseUrl, setBaseUrl] = useState(savedBaseUrl);
  const [token, setToken] = useState(savedToken);
  const [connected, setConnected] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [projects, setProjects] = useState<MobileProject[]>([]);
  const [threads, setThreads] = useState<MobileThread[]>([]);
  const [messages, setMessages] = useState<MobileMessage[]>([]);
  const [approvals, setApprovals] = useState<MobileApproval[]>([]);
  const [changedFiles, setChangedFiles] = useState<MobileChangedFile[]>([]);
  const [selectedProjectId, setSelectedProjectId] = useState('');
  const [selectedThreadId, setSelectedThreadId] = useState('');
  const [selectedFilePath, setSelectedFilePath] = useState('');
  const [diff, setDiff] = useState<MobileDiff | null>(null);
  const [prompt, setPrompt] = useState('');
  const [streamingText, setStreamingText] = useState('');
  const [activeRunId, setActiveRunId] = useState<string | null>(null);
  const streamDisposerRef = useRef<(() => void) | null>(null);

  const client = new MobileApiClient(baseUrl, token);

  useEffect(() => {
    return () => {
      streamDisposerRef.current?.();
    };
  }, []);

  async function connect(event?: FormEvent) {
    event?.preventDefault();
    try {
      window.localStorage.setItem('mobile.api.baseUrl', baseUrl);
      window.localStorage.setItem('mobile.api.token', token);
      const nextProjects = await client.listProjects();
      setProjects(nextProjects);
      setConnected(true);
      setError(null);
      if (nextProjects[0]) {
        await loadProject(nextProjects[0].id, nextProjects);
      }
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Failed to connect to API.');
    }
  }

  async function loadProject(projectId: string, projectList = projects) {
    setSelectedProjectId(projectId);
    const nextThreads = await client.listThreads(projectId);
    setThreads(nextThreads);
    const nextProject = projectList.find((project) => project.id === projectId);
    if (nextProject && !projects.some((project) => project.id === projectId)) {
      setProjects([...projectList]);
    }
    if (nextThreads[0]) {
      await loadThread(projectId, nextThreads[0].id);
    } else {
      setSelectedThreadId('');
      setMessages([]);
      setApprovals([]);
      setChangedFiles([]);
      setSelectedFilePath('');
      setDiff(null);
    }
  }

  async function loadThread(projectId: string, threadId: string) {
    streamDisposerRef.current?.();
    setSelectedThreadId(threadId);
    const [detail, nextApprovals, nextChangedFiles] = await Promise.all([
      client.getThread(projectId, threadId),
      client.listApprovals(threadId),
      client.listChangedFiles(projectId, threadId),
    ]);
    setMessages(detail.messages);
    setApprovals(nextApprovals);
    setChangedFiles(nextChangedFiles);

    if (nextChangedFiles[0]) {
      setSelectedFilePath(nextChangedFiles[0].path);
      setDiff(await client.getDiff(projectId, nextChangedFiles[0].path, threadId));
    } else {
      setSelectedFilePath('');
      setDiff(null);
    }

    streamDisposerRef.current = client.streamThread(threadId, (event) => {
      handleRunEvent(event, projectId, threadId);
    });
  }

  async function createThread() {
    if (!selectedProjectId) {
      return;
    }

    const detail = await client.createThread(selectedProjectId);
    setThreads((current) => [detail.thread, ...current]);
    await loadThread(selectedProjectId, detail.thread.id);
  }

  async function resolveApproval(approvalId: string, decision: 'Approved' | 'AlwaysAllow' | 'Rejected') {
    await client.resolveApproval(approvalId, decision);
    setApprovals(await client.listApprovals(selectedThreadId));
  }

  async function sendPrompt(event: FormEvent) {
    event.preventDefault();
    if (!selectedProjectId || !selectedThreadId || !prompt.trim()) {
      return;
    }

    const nextPrompt = prompt.trim();
    setMessages((current) => [
      ...current,
      {
        id: `local-${Date.now()}`,
        threadId: selectedThreadId,
        role: 'user',
        content: nextPrompt,
        timestamp: new Date().toISOString(),
      },
    ]);
    setPrompt('');
    const response = await client.startRun(selectedProjectId, selectedThreadId, nextPrompt);
    setActiveRunId(response.runId);
  }

  async function openDiff(filePath: string) {
    if (!selectedProjectId || !selectedThreadId) {
      return;
    }

    setSelectedFilePath(filePath);
    setDiff(await client.getDiff(selectedProjectId, filePath, selectedThreadId));
  }

  function handleRunEvent(event: MobileRunEvent, projectId: string, threadId: string) {
    if (event.kind === 'TextDelta') {
      setStreamingText((current) => `${current}${event.textDelta ?? ''}`);
      return;
    }

    if (event.kind === 'MessageCompleted') {
      setMessages((current) => [
        ...current,
        {
          id: `assistant-${event.runId}`,
          threadId,
          role: 'assistant',
          content: event.message ?? '',
          timestamp: event.timestamp,
        },
      ]);
      setStreamingText('');
      void client.listChangedFiles(projectId, threadId).then((files) => {
        setChangedFiles(files);
      });
      return;
    }

    if (event.kind === 'Completed') {
      setActiveRunId(null);
      setStreamingText('');
    }
  }

  return (
    <div className="mobile-shell">
      <header className="hero">
        <div>
          <p className="eyebrow">ClawSharp Mobile</p>
          <h1>Remote Runtime</h1>
          <p className="subtitle">Tauri mobile client scaffold for the shared ASP.NET API.</p>
        </div>
      </header>

      <section className="panel">
        <form className="stack" onSubmit={connect}>
          <label className="field">
            <span>API Base URL</span>
            <input value={baseUrl} onChange={(event) => setBaseUrl(event.target.value)} />
          </label>
          <label className="field">
            <span>Access Token</span>
            <input value={token} onChange={(event) => setToken(event.target.value)} placeholder="Optional for demo API" />
          </label>
          <button className="primary" type="submit">Connect</button>
          {error && <p className="error">{error}</p>}
        </form>
      </section>

      <main className="stack">
        <section className="panel">
          <div className="section-header">
            <h2>Projects</h2>
          </div>
          <div className="chip-row">
            {projects.map((project) => (
              <button
                key={project.id}
                className={project.id === selectedProjectId ? 'chip selected' : 'chip'}
                onClick={() => void loadProject(project.id)}
              >
                {project.name}
              </button>
            ))}
          </div>
        </section>

        <section className="panel">
          <div className="section-header">
            <h2>Threads</h2>
            <button className="secondary" onClick={() => void createThread()} disabled={!connected}>New</button>
          </div>
          <div className="list">
            {threads.map((thread) => (
              <button
                key={thread.id}
                className={thread.id === selectedThreadId ? 'list-item selected' : 'list-item'}
                onClick={() => void loadThread(thread.projectId, thread.id)}
              >
                <strong>{thread.title}</strong>
                <span>{thread.summary}</span>
              </button>
            ))}
          </div>
        </section>

        <section className="panel">
          <div className="section-header">
            <h2>Transcript</h2>
            {activeRunId && <span className="pill">Streaming</span>}
          </div>
          <div className="transcript">
            {messages.map((message) => (
              <article key={message.id} className={message.role === 'assistant' ? 'bubble assistant' : 'bubble user'}>
                <span className="role">{message.role}</span>
                <p>{message.content}</p>
              </article>
            ))}
            {streamingText && (
              <article className="bubble assistant">
                <span className="role">assistant</span>
                <p>{streamingText}</p>
              </article>
            )}
          </div>
          <form className="composer" onSubmit={sendPrompt}>
            <textarea
              value={prompt}
              onChange={(event) => setPrompt(event.target.value)}
              placeholder="Send a prompt to the remote runtime"
            />
            <button className="primary" type="submit">Send</button>
          </form>
        </section>

        <section className="panel">
          <div className="section-header">
            <h2>Approvals</h2>
          </div>
          <div className="list">
            {approvals.map((approval) => (
              <div key={approval.id} className="approval-card">
                <strong>{approval.action}</strong>
                <span>{approval.decision}</span>
                <div className="approval-actions">
                  <button className="secondary" onClick={() => void resolveApproval(approval.id, 'Approved')}>Approve</button>
                  <button className="secondary" onClick={() => void resolveApproval(approval.id, 'Rejected')}>Reject</button>
                </div>
              </div>
            ))}
            {approvals.length === 0 && <p className="muted">No pending approvals.</p>}
          </div>
        </section>

        <section className="panel">
          <div className="section-header">
            <h2>Review</h2>
          </div>
          <div className="chip-row">
            {changedFiles.map((file) => (
              <button
                key={file.path}
                className={file.path === selectedFilePath ? 'chip selected' : 'chip'}
                onClick={() => void openDiff(file.path)}
              >
                {file.path}
              </button>
            ))}
          </div>
          {diff && (
            <pre className="diff">
              {diff.hunks.map((hunk) => (
                <div key={hunk.header}>
                  <div>{hunk.header}</div>
                  {hunk.lines.map((line, index) => (
                    <div key={`${hunk.header}-${index}`}>{line.content}</div>
                  ))}
                </div>
              ))}
            </pre>
          )}
        </section>
      </main>
    </div>
  );
}
