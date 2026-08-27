import { HashRouter } from 'react-router-dom';
import { AppRoutes } from './app/router';
import { ErrorBoundary } from './components/feedback/ErrorBoundary';
import { PermissionProvider } from './app/permissions/PermissionProvider';

function App() {
  return (
    <ErrorBoundary>
      {/* HashRouter, not BrowserRouter: the Power Apps player hosts the app under a
          deep, dynamic path, so path-based routing lands on the catch-all. */}
      <HashRouter>
        <PermissionProvider>
          <AppRoutes />
        </PermissionProvider>
      </HashRouter>
    </ErrorBoundary>
  );
}

export default App;
