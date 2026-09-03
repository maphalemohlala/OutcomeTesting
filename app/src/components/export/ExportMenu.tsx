import { useId, useState } from 'react';
import { downloadCsv, downloadWorkbook, stampedFilename, type CellValue } from '../../lib/tabular';
import './ExportMenu.css';

interface Props {
  label: string;
  /** Filename stem; the download is dated so successive extracts do not overwrite. */
  stem: string;
  sheetName: string;
  headers: string[];
  rows: CellValue[][];
  /** One line describing exactly what will be written, so the file holds no surprises. */
  caption?: string;
  disabled?: boolean;
}

/**
 * Excel and CSV download of a table the caller already has on screen. Because the rows
 * come from data Dataverse has already returned, a user can only ever export what they
 * are permitted to read (BR-012) — this control is not an access path of its own.
 */
export function ExportMenu({ label, stem, sheetName, headers, rows, caption, disabled }: Props) {
  const [open, setOpen] = useState(false);
  const menuId = useId();
  const empty = rows.length === 0;

  function run(format: 'xlsx' | 'csv') {
    setOpen(false);
    if (format === 'xlsx') {
      downloadWorkbook(stampedFilename(stem, 'xlsx'), [{ name: sheetName, headers, rows }]);
    } else {
      downloadCsv(stampedFilename(stem, 'csv'), headers, rows);
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
