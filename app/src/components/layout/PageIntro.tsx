import './PageIntro.css';

interface PageIntroProps {
  title: string;
  purpose: string;
  actions?: React.ReactNode;
}

export function PageIntro({ title, purpose, actions }: PageIntroProps) {
  return (
    <div className="page-intro">
      <div>
        <h1>{title}</h1>
        <p className="page-intro__purpose">{purpose}</p>
      </div>
      {actions ? <div className="page-intro__actions">{actions}</div> : null}
    </div>
  );
}
