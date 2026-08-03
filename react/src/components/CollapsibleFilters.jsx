import React, { useState } from 'react';

export default function CollapsibleFilters({ id, children, buttonClassName = '', activeCount = 0 }) {
  const [isOpen, setIsOpen] = useState(() => activeCount > 0);
  const countLabel = activeCount > 0 ? ` (${activeCount} active)` : '';
  const buttonLabel = `${isOpen ? 'Hide filters' : 'Show filters'}${countLabel}`;

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
