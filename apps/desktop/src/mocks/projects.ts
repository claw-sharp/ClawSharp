import { Project } from '@/types';

export const mockProjects: Project[] = [
  {
    id: 'proj-1',
    name: 'ClawSharp',
    path: '~/code/clawsharp',
    branch: 'main',
    activeThreadCount: 3,
    lastUpdated: '2026-04-07T10:30:00Z',
    description: 'Core agent runtime and CLI',
  },
  {
    id: 'proj-2',
    name: 'ClawSharp Docs',
    path: '~/code/clawsharp-docs',
    branch: 'feat/web-guide',
    activeThreadCount: 2,
    lastUpdated: '2026-04-07T09:15:00Z',
    description: 'Documentation site and guides',
  },
  {
    id: 'proj-3',
    name: 'ClawSharp Playground',
    path: '~/code/clawsharp-playground',
    branch: 'develop',
    activeThreadCount: 1,
    lastUpdated: '2026-04-06T18:45:00Z',
    description: 'Interactive playground and examples',
  },
  {
    id: 'proj-4',
    name: 'CS Desktop Prototype',
    path: '~/code/cs-desktop',
    branch: 'prototype/v1',
    activeThreadCount: 0,
    lastUpdated: '2026-04-05T14:20:00Z',
    description: 'Desktop app prototype with Tauri',
  },
];
