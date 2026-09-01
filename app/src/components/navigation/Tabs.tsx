import { useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import './Tabs.css';

export interface TabItem {
  id: string;
  label: string;
  /** Shown beside the label so volumes are visible without opening each tab. */
  count?: number;
  render: () => ReactNode;
}

/**
 * Tabbed region following the WAI-ARIA tabs pattern with automatic activation:
 * arrow keys move focus and select, because every panel is already loaded.
 */
export function Tabs({ label, items }: { label: string; items: TabItem[] }) {
  const [activeId, setActiveId] = useState<string>(items[0]?.id ?? '');
  const buttons = useRef<Record<string, HTMLButtonElement | null>>({});

  // Fall back to the first tab if the selected one is no longer present.
  const active = items.some((item) => item.id === activeId) ? activeId : (items[0]?.id ?? '');
  if (active !== activeId) setActiveId(active);

  if (items.length === 0) return null;

  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const index = items.findIndex((item) => item.id === active);
    if (index < 0) return;
    let next: number;
    if (event.key === 'ArrowRight') next = (index + 1) % items.length;
    else if (event.key === 'ArrowLeft') next = (index - 1 + items.length) % items.length;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = items.length - 1;
    else return;
    event.preventDefault();
    const id = items[next].id;
    setActiveId(id);
    buttons.current[id]?.focus();
  }

  const activeItem = items.find((item) => item.id === active);

  return (
    <div className="tabs">
      <div className="tabs__list" role="tablist" aria-label={label} onKeyDown={onKeyDown}>
        {items.map((item) => {
          const selected = item.id === active;
          return (
            <button
              key={item.id}
              ref={(element) => {
                buttons.current[item.id] = element;
              }}
              type="button"
              role="tab"
              id={`tab-${item.id}`}
              className="tabs__tab"
              aria-selected={selected}
              aria-controls={`tabpanel-${item.id}`}
              tabIndex={selected ? 0 : -1}
              onClick={() => setActiveId(item.id)}
            >
              {item.label}
              {item.count != null ? <span className="tabs__count">{item.count}</span> : null}
            </button>
          );
        })}
      </div>
      {activeItem ? (
        <div
          className="tabs__panel"
          role="tabpanel"
          id={`tabpanel-${activeItem.id}`}
          aria-labelledby={`tab-${activeItem.id}`}
          tabIndex={0}
        >
          {activeItem.render()}
        </div>
      ) : null}
    </div>
  );
}
