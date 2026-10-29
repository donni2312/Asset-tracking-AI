import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Toaster } from '@/components/ui/toaster';
import { Route, Switch, Router as WouterRouter } from 'wouter';
import NotFound from '@/pages/not-found';

import Dashboard from './pages/dashboard';
import SbuList from './pages/sbus/list';
import AssetList from './pages/assets/list';
import NewAsset from './pages/assets/new';
import AssetDetail from './pages/assets/detail';
import EditAsset from './pages/assets/edit';
import LocationList from './pages/locations/list';
import NewLocation from './pages/locations/new';
import EditLocation from './pages/locations/edit';
import MovementList from './pages/movements/list';
import NewMovement from './pages/movements/new';
import GatePassList from './pages/gate-passes/list';
import RfidSimulator from './pages/rfid-simulator';
import Insights from './pages/insights';
import BcSync from './pages/bc-sync';
import Reports from './pages/reports';
import AssetCardPage from './pages/assets/card';
import AssetRegister from './pages/finance/register';
import CategoryReport from './pages/finance/category-report';
import AssetPosition from './pages/finance/asset-position';
import Approvals from './pages/finance/approvals';
import Verification from './pages/finance/verification';
import Administration from './pages/finance/administration';
import Planned from './pages/planned';
import Scan from './pages/scan';
import { Departments, People } from './pages/people';
import { SignInScreen } from './components/sign-in';
import { useCurrentUser } from './hooks/use-auth';

/**
 * Each role's start page: finance roles and auditors the asset position (AU-10), the Gate Officer the scan screen,
 * the Employee their items, the System Administrator the administration screen, everyone else the operations dashboard.
 */
function Home() {
  const user = useCurrentUser();
  if (!user) return null;
  if (['group_finance', 'sbu_finance', 'auditor'].includes(user.role)) return <AssetPosition />;
  if (user.role === 'gate_officer') return <Scan />;
  if (user.role === 'employee') return <AssetList />;
  if (user.role === 'system_admin') return <Administration />;
  return <Dashboard />;
}

const queryClient = new QueryClient();

function Router() {
  // Phase 2: every screen needs a sign-in (the API answers 401 otherwise).
  const user = useCurrentUser();
  if (!user) return <SignInScreen />;
  return (
    <Switch>
      <Route path="/" component={Home} />
      <Route path="/operations" component={Dashboard} />
      <Route path="/asset-position" component={AssetPosition} />
      <Route path="/register" component={AssetRegister} />
      <Route path="/reports/category" component={CategoryReport} />
      <Route path="/approvals" component={Approvals} />
      <Route path="/verification" component={Verification} />
      <Route path="/administration" component={Administration} />
      <Route path="/stores">{() => <Planned screen="stores" />}</Route>
      <Route path="/people" component={People} />
      <Route path="/departments" component={Departments} />
      <Route path="/scan" component={Scan} />
      <Route path="/fleet">{() => <Planned screen="fleet" />}</Route>
      <Route path="/sbus" component={SbuList} />
      <Route path="/assets" component={AssetList} />
      <Route path="/assets/new" component={NewAsset} />
      <Route path="/assets/:id/edit" component={EditAsset} />
      <Route path="/assets/:id/card" component={AssetCardPage} />
      <Route path="/assets/:id" component={AssetDetail} />
      <Route path="/locations" component={LocationList} />
      <Route path="/locations/new" component={NewLocation} />
      <Route path="/locations/:id/edit" component={EditLocation} />
      <Route path="/movements" component={MovementList} />
      <Route path="/movements/new" component={NewMovement} />
      <Route path="/gate-passes" component={GatePassList} />
      <Route path="/rfid-simulator" component={RfidSimulator} />
      <Route path="/insights" component={Insights} />
      <Route path="/bc-sync" component={BcSync} />
      <Route path="/reports" component={Reports} />
      <Route component={NotFound} />
    </Switch>
  );
}

function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <WouterRouter base={import.meta.env.BASE_URL.replace(/\/$/, '')}>
        <Router />
      </WouterRouter>
      <Toaster />
    </QueryClientProvider>
  );
}

export default App;
