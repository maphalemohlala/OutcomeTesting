import { useId, useState } from 'react';
import { downloadCsv, downloadWorkbook, stampedFilename, type CellValue } from '../../lib/tabular';
import './ExportMenu.css';

interface Props {
  label: string;
  /** Filename stem; the download is dated so successive extracts do not overwrite. */
  stem: string;
  /**
   * The whole filename, extension and all, for a download whose name is fixed by whatever
   * receives it rather than by us. Trail Light is one: `DFALIN1_outcometesting_yyyy_mm_dd`
   * is the receiving end's name for that feed (AD-216), so the stem-plus-date convention
   * below must not be applied to it. Given this, `stem` is ignored.
   */
  filenameFor?: (format: 'xlsx' | 'csv') => string;
  sheetName: string;
  headers: string[];
  rows: CellValue[][];
  /** One line describing exactly what will be written, so the file holds no surprises. */
  caption?: string;
  disabled?: boolean;
  /**
   * Why there is nothing to download, shown on the disabled trigger. Without it an
   * empty table renders a dead control and the reason lives only in the caller's head.
   */
  emptyHint?: string;
}

/**
 * Excel and CSV download of a table the caller already has on screen. Because the rows
 * come from data Dataverse has already returned, a user can only ever export what they
 * are permitted to read (BR-012) — this control is not an access path of its own.
 */
export function ExportMenu({
  label,
  stem,
  filenameFor,
  sheetName,
  headers,
  rows,
  caption,
  disabled,
  emptyHint,
}: Props) {
  const [open, setOpen] = useState(false);
  const menuId = useId();
  const empty = rows.length === 0;

  function run(format: 'xlsx' | 'csv') {
    setOpen(false);
    const name = filenameFor ? filenameFor(format) : stampedFilename(stem, format);
    if (format === 'xlsx') {
      downloadWorkbook(name, [{ name: sheetName, headers, rows }]);
    } else {
      downloadCsv(name, headers, rows);
    }
  }

  return (
    <div
      className="export-menu"
      onKeyDown={(e) => {
        if (e.key === 'Escape' && open) {
          setOpen(false);
          e.currentTarget.querySelector<HTMLButtonElement>('.export-menu__trigger')?.focus();
        }
      }}
      onBlur={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget as Node)) setOpen(false);
      }}
    >
      <button
        type="button"
        className="export-menu__trigger"
        aria-expanded={open}
        aria-controls={menuId}
        disabled={disabled || empty}
        title={empty ? (emptyHint ?? 'There is nothing to download yet.') : undefined}
        onClick={() => setOpen((current) => !current)}
      >
        {label}
      </button>
      <div id={menuId} className="export-menu__panel" hidden={!open}>
        {caption ? <p className="export-menu__caption">{caption}</p> : null}
        <button type="button" className="export-menu__item" onClick={() => run('xlsx')}>
          Excel workbook (.xlsx)
        </button>
        <button type="button" className="export-menu__item" onClick={() => run('csv')}>
          Comma-separated values (.csv)
        </button>
      </div>
    </div>
  );
}
