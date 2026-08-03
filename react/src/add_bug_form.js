export const BUG_TYPES = [
  { value: 'page_not_loading', label: 'Page not loading' },
  { value: 'form_submission', label: 'Form submission' },
  { value: 'crash', label: 'Crash' },
  { value: 'api', label: 'API' },
  { value: 'database', label: 'Database' }
];

export const SEVERITIES = [
  { value: 'low', label: 'Low' },
  { value: 'mid', label: 'Mid' },
  { value: 'high', label: 'High' },
  { value: 'urgent', label: 'Urgent' }
];

export const PRIORITIES = [
  { value: 'p0', label: 'P0 - Now' },
  { value: 'p1', label: 'P1 - Next' },
  { value: 'p2', label: 'P2 - Normal' },
  { value: 'p3', label: 'P3 - Later' }
];

export const CORE_TAGS = [
  { value: 'front-end', label: 'Front-end' },
  { value: 'back-end', label: 'Back-end' }
];

export const CORE_TAG_VALUES = CORE_TAGS.map((tag) => tag.value);

export const FREQUENCIES = [
  { value: 'unknown', label: 'Unknown' },
  { value: 'once', label: 'Once' },
  { value: 'intermittent', label: 'Intermittent' },
  { value: 'frequent', label: 'Frequent' },
  { value: 'always', label: 'Always' }
];

export const MAX_TEXT_EVIDENCE_FILES = 3;
const MAX_TEXT_EVIDENCE_BYTES = 100_000;

export const ADD_PROJECT_OPTION = '__add_project__';

export function createInitialBugForm(projectId = '') {
  return {
    issueTitle: '',
    bugType: 'page_not_loading',
    projectId,
    assigneeUserId: '',
    severity: 'mid',
    priority: 'p2',
    tags: [],
    environment: '',
    expectedBehavior: '',
    actualBehavior: '',
    stepsToReproduce: '',
    frequency: 'unknown',
    textEvidence: []
  };
}

export function validateForm(form) {
  const errors = {};

  if (!form.issueTitle.trim()) {
    errors.issueTitle = 'Issue title is required.';
  }

  if (!form.description.trim()) {
    errors.description = 'Description is required.';
  }

  if (!form.bugType) {
    errors.bugType = 'Bug type is required.';
  }

  if (!form.severity) {
    errors.severity = 'Severity is required.';
  }

  if (!form.priority) {
    errors.priority = 'Priority is required.';
  }

  if (!form.projectId) {
    errors.projectId = 'Project is required.';
  } else if (form.projectId === ADD_PROJECT_OPTION) {
    errors.projectId = 'Create the new project before submitting the bug.';
  }

  const tags = Array.isArray(form.tags) ? form.tags : [];
  const selectedCoreTags = CORE_TAG_VALUES.filter((tag) => tags.includes(tag));
  if (selectedCoreTags.length === 0) {
    errors.tags = 'Choose front-end or back-end.';
  } else if (selectedCoreTags.length > 1) {
    errors.tags = 'Choose front-end or back-end, not both.';
  }

  return errors;
}

export function normalizeTags(tags) {
  const normalized = [];
  let hasCoreTag = false;

  for (const tag of tags) {
    if (CORE_TAG_VALUES.includes(tag)) {
      if (!hasCoreTag) {
        normalized.push(tag);
        hasCoreTag = true;
      }
      continue;
    }

    if (!normalized.includes(tag)) {
      normalized.push(tag);
    }
  }

  return normalized;
}

export function buildCreatePayload(form, reportBuilder) {
  const reportPayload = reportBuilder.toPayload();
  const payload = {
    issueTitle: form.issueTitle.trim(),
    description: reportPayload.text.trim(),
    bugType: form.bugType,
    projectId: form.projectId,
    severity: form.severity,
    priority: form.priority,
    tags: normalizeTags(form.tags),
    environment: form.environment.trim() || null,
    expectedBehavior: form.expectedBehavior.trim() || null,
    actualBehavior: form.actualBehavior.trim() || null,
    stepsToReproduce: form.stepsToReproduce.trim() || null,
    frequency: form.frequency,
    textEvidence: form.textEvidence,
    reportImages: reportPayload.images
  };

  if (form.assigneeUserId) {
    payload.assigneeUserId = form.assigneeUserId;
  }

  return payload;
}

export async function fileToTextEvidence(file) {
  if (file.type && file.type !== 'text/plain') {
    throw new Error('Attach .txt evidence files only.');
  }

  if (!file.name.toLowerCase().endsWith('.txt')) {
    throw new Error('Text evidence must use the .txt extension.');
  }

  if (file.size > MAX_TEXT_EVIDENCE_BYTES) {
    throw new Error('Text evidence files must be 100 KB or smaller.');
  }

  const text = await file.text();
  if (!text.trim()) {
    throw new Error('Text evidence file cannot be empty.');
  }

  return {
    name: file.name,
    contentType: 'text/plain',
    text
  };
}
