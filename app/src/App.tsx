import { BrowserRouter } from 'react-router-dom';
import { AppRoutes } from './app/router';
import { ErrorBoundary } from './components/feedback/ErrorBoundary';

function App() {
  return (
    <ErrorBoundary>
      <BrowserRouter>
        <AppRoutes />
      </BrowserRouter>
    </ErrorBoundary>
  );
}

export default App;
