import React, { useState } from 'react';
import { useI18n } from '../i18n';

export default function CollapsibleFilters({ id, children, buttonClassName = '', activeCount = 0 }) {
  const { t } = useI18n();
  const [isOpen, setIsOpen] = useState(() => activeCount > 0);
  const countLabel = activeCount > 0 ? t('filters.activeCount', ' ({{count}} active)', { count: activeCount }) : '';
  const buttonLabel = `${isOpen ? t('filters.hide', 'Hide filters') : t('filters.show', 'Show filters')}${countLabel}`;

  return (
    <div className="filter-collapse">
      <button
        type="button"
        className={['filter-toggle', buttonClassName].filter(Boolean).join(' ')}
        aria-expanded={isOpen}
        aria-controls={id}
        onClick={() => setIsOpen((current) => !current)}
      >
        {buttonLabel}
      </button>
      <div id={id} className="filter-collapse-panel" hidden={!isOpen}>
        {children}
      </div>
    </div>
  );
}
