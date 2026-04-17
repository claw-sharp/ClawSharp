import { useEffect, useMemo, useState } from 'react';
import { Bot, FileCode2, Plus, RefreshCw, Sparkles } from 'lucide-react';
import { toast } from '@/components/ui/sonner';
import { Checkbox } from '@/components/ui/checkbox';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { cn } from '@/lib/utils';
import { useAppStore } from '@/store';
import type { Agent, AgentDraft } from '@/types';

const emptyDraft: AgentDraft = {
  identifier: '',
  whenToUse: '',
  systemPrompt: '',
  model: null,
  color: null,
  tools: [],
  disallowedTools: [],
  skills: [],
  permissionMode: null,
  maxTurns: null,
  background: false,
  initialPrompt: null,
  memory: null,
  isolation: null,
  omitClaudeMd: false,
};

export const AgentsPanel = () => {
  const {
    projects,
    selectedProjectId,
    agents,
    agentsLoading,
    agentsError,
    loadAgents,
    createAgent,
    proposeAgent,
    openExternalEditor,
  } = useAppStore();
  const [selectedAgentId, setSelectedAgentId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [createDialogOpen, setCreateDialogOpen] = useState(false);
  const [proposalPrompt, setProposalPrompt] = useState('');
  const [draft, setDraft] = useState<AgentDraft>(emptyDraft);
  const [openInEditor, setOpenInEditor] = useState(true);
  const [createError, setCreateError] = useState<string | null>(null);
  const [creatingAgent, setCreatingAgent] = useState(false);
  const [proposingAgent, setProposingAgent] = useState(false);

  const selectedProject = projects.find((project) => project.id === selectedProjectId) ?? null;

  useEffect(() => {
    if (selectedProjectId) {
      void loadAgents(selectedProjectId);
    }
  }, [loadAgents, selectedProjectId]);

  const filteredAgents = useMemo(() => {
    const query = search.trim().toLowerCase();
    return agents.filter((agent) => {
      if (!query) {
        return true;
      }

      return [
        agent.identifier,
        agent.whenToUse,
        agent.source,
        agent.model,
      ]
        .filter(Boolean)
        .some((value) => String(value).toLowerCase().includes(query));
    });
  }, [agents, search]);

  const selectedAgent = filteredAgents.find((agent) => agent.identifier === selectedAgentId)
    ?? agents.find((agent) => agent.identifier === selectedAgentId)
    ?? filteredAgents[0]
    ?? agents[0]
    ?? null;

  useEffect(() => {
    if (!selectedAgent) {
      setSelectedAgentId(null);
      return;
    }

    setSelectedAgentId(selectedAgent.identifier);
  }, [selectedAgent]);

  const projectAgentCount = agents.filter((agent) => agent.source === 'projectSettings').length;
  const builtInAgentCount = agents.filter((agent) => agent.source === 'built-in').length;
  const canCreateAgent = !creatingAgent &&
    validateAgentIdentifier(draft.identifier, agents, selectedAgent?.identifier ?? null) === null &&
    draft.whenToUse.trim().length > 0 &&
    draft.systemPrompt.trim().length > 0;

  const resetCreateDialog = () => {
    setProposalPrompt('');
    setDraft(emptyDraft);
    setOpenInEditor(true);
    setCreateError(null);
    setCreatingAgent(false);
    setProposingAgent(false);
  };

  const handleCreateDialogChange = (open: boolean) => {
    setCreateDialogOpen(open);
    if (!open) {
      resetCreateDialog();
    }
  };

  const handleDraftChange = <T extends keyof AgentDraft>(key: T, value: AgentDraft[T]) => {
    setDraft((current) => ({ ...current, [key]: value }));
    setCreateError(null);
  };

  const handleProposeAgent = async () => {
    if (!proposalPrompt.trim()) {
      setCreateError('Describe the agent you want before generating a draft.');
      return;
    }

    setProposingAgent(true);
    setCreateError(null);
    const proposed = await proposeAgent(proposalPrompt, draft.model ?? null);
    setProposingAgent(false);
    if (!proposed) {
      setCreateError('Agent proposal failed. Check the panel status and try again.');
      return;
    }

    setDraft((current) => ({
      ...current,
      ...proposed,
      model: current.model ?? proposed.model ?? null,
    }));
    toast.success(`Drafted @${proposed.identifier}`);
  };

  const handleCreateAgent = async () => {
    const identifierError = validateAgentIdentifier(draft.identifier, agents, null);
    if (identifierError) {
      setCreateError(identifierError);
      return;
    }

    if (!draft.whenToUse.trim()) {
      setCreateError('Usage guidance is required.');
      return;
    }

    if (!draft.systemPrompt.trim()) {
      setCreateError('System prompt is required.');
      return;
    }

    setCreatingAgent(true);
    setCreateError(null);
    const createdAgent = await createAgent({
      ...draft,
      identifier: draft.identifier.trim(),
      whenToUse: draft.whenToUse.trim(),
      systemPrompt: draft.systemPrompt.trim(),
      model: draft.model?.trim() || null,
      color: draft.color?.trim() || null,
      permissionMode: draft.permissionMode?.trim() || null,
      initialPrompt: draft.initialPrompt?.trim() || null,
      memory: draft.memory?.trim() || null,
      isolation: draft.isolation?.trim() || null,
    });
    setCreatingAgent(false);

    if (!createdAgent) {
      setCreateError('Agent creation failed. Check the panel status and try again.');
      return;
    }

    setSelectedAgentId(createdAgent.identifier);
    if (openInEditor && createdAgent.filePath) {
      void openExternalEditor({
        kind: 'file',
        path: createdAgent.filePath,
      });
    }

    toast.success(`Created @${createdAgent.identifier}`, {
      description: createdAgent.filePath || createdAgent.baseDirectory,
    });
    handleCreateDialogChange(false);
  };

  if (!selectedProjectId || !selectedProject) {
    return (
      <div className="flex h-full items-center justify-center p-6">
        <div className="max-w-md rounded-xl border border-border surface-1 p-6 text-center">
          <Bot className="mx-auto h-8 w-8 text-muted-foreground" />
          <h2 className="mt-4 text-lg font-semibold text-foreground">Agents</h2>
          <p className="mt-2 text-sm text-muted-foreground">
            Open a project to inspect available agents, draft a new one, and save project-specific agent definitions.
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="flex h-full min-h-0 flex-col gap-4 p-4 lg:p-6">
      <div className="rounded-2xl border border-border bg-card p-4 shadow-sm">
        <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
          <div>
            <div className="text-xs font-semibold uppercase tracking-[0.22em] text-muted-foreground">Agents</div>
            <div className="mt-2 text-2xl font-semibold text-foreground">{selectedProject.name}</div>
            <div className="mt-1 text-sm text-muted-foreground">{selectedProject.path}</div>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <StatusPill label={`${agents.length} total`} tone="neutral" />
            <StatusPill label={`${builtInAgentCount} built-in`} tone="neutral" />
            <StatusPill label={`${projectAgentCount} project`} tone="success" />
            <button
              onClick={() => setCreateDialogOpen(true)}
              className="inline-flex items-center gap-2 rounded-md bg-primary px-3 py-2 text-sm font-medium text-primary-foreground transition-colors hover:opacity-90"
            >
              <Plus className="h-4 w-4" />
              Add agent
            </button>
            <button
              onClick={() => void loadAgents(selectedProjectId, { force: true })}
              disabled={agentsLoading}
              className="inline-flex items-center gap-2 rounded-md border border-border px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent disabled:cursor-not-allowed disabled:opacity-60"
            >
              <RefreshCw className={cn('h-4 w-4', agentsLoading && 'animate-spin')} />
              Refresh
            </button>
          </div>
        </div>
        {agentsError && (
          <div className="mt-4 rounded-md border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">
            {agentsError}
          </div>
        )}
      </div>

      <div className="grid min-h-0 flex-1 gap-4 lg:grid-cols-[22rem_minmax(0,1fr)]">
        <div className="flex min-h-0 flex-col rounded-2xl border border-border bg-card shadow-sm">
          <div className="border-b border-border p-4">
            <input
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Search agents..."
              className="w-full rounded-md border border-border bg-input px-3 py-2 text-sm text-foreground outline-none focus:border-primary/40"
            />
          </div>
          <div className="flex-1 overflow-y-auto p-3">
            {filteredAgents.length === 0 ? (
              <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
                No agents match the current filters.
              </div>
            ) : (
              <div className="space-y-2">
                {filteredAgents.map((agent) => (
                  <button
                    key={agent.identifier}
                    onClick={() => setSelectedAgentId(agent.identifier)}
                    className={cn(
                      'w-full rounded-xl border p-3 text-left transition-colors',
                      agent.identifier === selectedAgent?.identifier
                        ? 'border-primary/40 bg-primary/5'
                        : 'border-border hover:bg-accent/40'
                    )}
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <div className="text-sm font-semibold text-foreground">@{agent.identifier}</div>
                        <div className="mt-1 text-xs text-muted-foreground">{formatAgentSource(agent.source)}</div>
                      </div>
                      <div className="flex flex-wrap justify-end gap-2">
                        {agent.model && <StatusPill label={agent.model} tone="neutral" />}
                        {agent.background && <StatusPill label="Background" tone="success" />}
                      </div>
                    </div>
                    <div className="mt-3 text-xs leading-5 text-muted-foreground line-clamp-3">
                      {agent.whenToUse}
                    </div>
                  </button>
                ))}
              </div>
            )}
          </div>
        </div>

        <div className="min-h-0 overflow-y-auto rounded-2xl border border-border bg-card shadow-sm">
          {!selectedAgent ? (
            <div className="flex h-full items-center justify-center p-6 text-sm text-muted-foreground">
              No agent selected.
            </div>
          ) : (
            <div className="space-y-6 p-5">
              <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <h2 className="text-2xl font-semibold text-foreground">@{selectedAgent.identifier}</h2>
                    <StatusPill label={formatAgentSource(selectedAgent.source)} tone="neutral" />
                    {selectedAgent.model && <StatusPill label={selectedAgent.model} tone="neutral" />}
                    {selectedAgent.background && <StatusPill label="Background" tone="success" />}
                    {selectedAgent.permissionMode && <StatusPill label={selectedAgent.permissionMode} tone="neutral" />}
                  </div>
                  <div className="mt-2 whitespace-pre-wrap text-sm text-muted-foreground">
                    {selectedAgent.whenToUse}
                  </div>
                </div>
                {selectedAgent.filePath && (
                  <button
                    onClick={() => void openExternalEditor({ kind: 'file', path: selectedAgent.filePath! })}
                    className="inline-flex items-center gap-2 rounded-md border border-border px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent"
                  >
                    <FileCode2 className="h-4 w-4" />
                    Open file
                  </button>
                )}
              </div>

              <Section title="Metadata">
                <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
                  <MetadataRow label="Source" value={formatAgentSource(selectedAgent.source)} />
                  <MetadataRow label="Directory" value={selectedAgent.baseDirectory} />
                  <MetadataRow label="File" value={selectedAgent.filePath || 'built-in'} />
                  <MetadataRow label="Tools" value={joinList(selectedAgent.tools, 'All tools')} />
                  <MetadataRow label="Blocked tools" value={joinList(selectedAgent.disallowedTools, 'None')} />
                  <MetadataRow label="Skills" value={joinList(selectedAgent.skills, 'None')} />
                  <MetadataRow label="Color" value={selectedAgent.color || 'n/a'} />
                  <MetadataRow label="Memory" value={selectedAgent.memory || 'n/a'} />
                  <MetadataRow label="Isolation" value={selectedAgent.isolation || 'n/a'} />
                </div>
              </Section>

              <Section title="System Prompt">
                <div className="rounded-xl border border-border bg-muted/20 p-4">
                  <pre className="whitespace-pre-wrap break-words text-sm leading-6 text-foreground">
                    {selectedAgent.systemPrompt}
                  </pre>
                </div>
              </Section>
            </div>
          )}
        </div>
      </div>

      <Dialog open={createDialogOpen} onOpenChange={handleCreateDialogChange}>
        <DialogContent className="sm:max-w-4xl">
          <DialogHeader>
            <DialogTitle>Add Agent</DialogTitle>
            <DialogDescription>
              Create a project agent under `{selectedProject.path}/.clawsharp/agents`. Draft from a natural-language request, then edit before saving.
            </DialogDescription>
          </DialogHeader>

          <div className="grid gap-6 lg:grid-cols-[1.1fr_1fr]">
            <div className="space-y-4 rounded-2xl border border-border bg-muted/10 p-4">
              <div className="space-y-2">
                <Label htmlFor="agent-proposal">Describe the agent</Label>
                <Textarea
                  id="agent-proposal"
                  value={proposalPrompt}
                  onChange={(event) => {
                    setProposalPrompt(event.target.value);
                    setCreateError(null);
                  }}
                  rows={8}
                  placeholder="Example: Create an agent that reviews recent frontend changes, checks for accessibility regressions, and returns a short list of issues."
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="agent-model">Optional model override</Label>
                <Input
                  id="agent-model"
                  value={draft.model ?? ''}
                  onChange={(event) => handleDraftChange('model', event.target.value)}
                  placeholder="inherit the project default"
                />
              </div>
              <button
                onClick={() => void handleProposeAgent()}
                disabled={proposingAgent || !proposalPrompt.trim()}
                className="inline-flex items-center gap-2 rounded-md bg-primary px-3 py-2 text-sm font-medium text-primary-foreground transition-colors hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-60"
              >
                <Sparkles className="h-4 w-4" />
                {proposingAgent ? 'Generating draft…' : 'Propose draft'}
              </button>
              <div className="rounded-xl border border-dashed border-border p-3 text-xs leading-5 text-muted-foreground">
                The draft generator uses the current project context so the proposed prompt can align with local conventions.
              </div>
            </div>

            <div className="grid gap-4">
              <div className="grid gap-2">
                <Label htmlFor="agent-identifier">Identifier</Label>
                <Input
                  id="agent-identifier"
                  value={draft.identifier}
                  onChange={(event) => handleDraftChange('identifier', event.target.value)}
                  placeholder="release-notes-writer"
                  autoFocus
                />
                <div className="text-xs text-muted-foreground">
                  Use letters, numbers, and `-` only. The runtime will expose this as the agent type.
                </div>
                {validateAgentIdentifier(draft.identifier, agents, null) && (
                  <div className="text-xs text-destructive">
                    {validateAgentIdentifier(draft.identifier, agents, null)}
                  </div>
                )}
              </div>

              <div className="grid gap-2">
                <Label htmlFor="agent-when">When to use</Label>
                <Textarea
                  id="agent-when"
                  value={draft.whenToUse}
                  onChange={(event) => handleDraftChange('whenToUse', event.target.value)}
                  rows={6}
                  placeholder="Use this agent when..."
                />
              </div>

              <div className="grid gap-2">
                <Label htmlFor="agent-system-prompt">System prompt</Label>
                <Textarea
                  id="agent-system-prompt"
                  value={draft.systemPrompt}
                  onChange={(event) => handleDraftChange('systemPrompt', event.target.value)}
                  rows={14}
                  placeholder="You are..."
                />
              </div>

              <label className="flex items-center gap-3 rounded-xl border border-border px-3 py-2 text-sm text-foreground">
                <Checkbox
                  checked={openInEditor}
                  onCheckedChange={(checked) => setOpenInEditor(checked === true)}
                />
                Open the new agent file in the external editor after creating it
              </label>

              {createError && (
                <div className="rounded-md border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">
                  {createError}
                </div>
              )}
            </div>
          </div>

          <DialogFooter>
            <button
              onClick={() => handleCreateDialogChange(false)}
              className="rounded-md border border-border px-3 py-2 text-sm text-foreground transition-colors hover:bg-accent"
            >
              Cancel
            </button>
            <button
              onClick={() => void handleCreateAgent()}
              disabled={!canCreateAgent}
              className="rounded-md bg-primary px-3 py-2 text-sm font-medium text-primary-foreground transition-colors hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-60"
            >
              {creatingAgent ? 'Creating…' : 'Create agent'}
            </button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
};

const Section = ({ title, children }: { title: string; children: React.ReactNode }) => (
  <section>
    <div className="mb-3 text-xs font-semibold uppercase tracking-[0.22em] text-muted-foreground">{title}</div>
    {children}
  </section>
);

const StatusPill = ({ label, tone }: { label: string; tone: 'neutral' | 'success' }) => (
  <span
    className={cn(
      'inline-flex rounded-full px-2.5 py-1 text-xs font-medium',
      tone === 'success' && 'bg-emerald-500/15 text-emerald-700 dark:text-emerald-300',
      tone === 'neutral' && 'bg-accent text-muted-foreground'
    )}
  >
    {label}
  </span>
);

const MetadataRow = ({ label, value }: { label: string; value: string }) => (
  <div className="rounded-xl border border-border p-4">
    <div className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">{label}</div>
    <div className="mt-2 break-words text-sm text-foreground">{value}</div>
  </div>
);

function validateAgentIdentifier(value: string, agents: Agent[], currentIdentifier: string | null): string | null {
  const trimmed = value.trim();
  if (!trimmed) {
    return 'Agent identifier is required.';
  }

  if (!/^[A-Za-z0-9][A-Za-z0-9-]*[A-Za-z0-9]$/.test(trimmed)) {
    return 'Use letters, numbers, and hyphens only. The identifier must start and end with a letter or number.';
  }

  if (trimmed.length < 3) {
    return 'Agent identifier must be at least 3 characters long.';
  }

  if (trimmed.length > 50) {
    return 'Agent identifier must be 50 characters or fewer.';
  }

  if (agents.some((agent) => agent.identifier.toLowerCase() === trimmed.toLowerCase() && agent.identifier !== currentIdentifier)) {
    return 'That agent identifier is already in use.';
  }

  return null;
}

function formatAgentSource(source: string): string {
  switch (source) {
    case 'projectSettings':
      return 'project';
    case 'userSettings':
      return 'user';
    case 'policySettings':
      return 'policy';
    case 'built-in':
      return 'built-in';
    default:
      return source;
  }
}

function joinList(values: string[] | undefined, fallback: string): string {
  return values && values.length > 0 ? values.join(', ') : fallback;
}
