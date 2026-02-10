import { createBrowserRouter, Navigate, RouterProvider } from "react-router";
import { AppLayout } from "@/components/layout/AppLayout";
import AgentListPage from "@/pages/AgentListPage";
import AgentCreatePage from "@/pages/AgentCreatePage";
import AgentDetailPage from "@/pages/AgentDetailPage";
import AgentSearchPage from "@/pages/AgentSearchPage";
import ProviderListPage from "@/pages/ProviderListPage";
import ProviderCreatePage from "@/pages/ProviderCreatePage";
import ProviderDetailPage from "@/pages/ProviderDetailPage";
import NotFoundPage from "@/pages/NotFoundPage";

const router = createBrowserRouter([
  {
    element: <AppLayout />,
    children: [
      { index: true, element: <Navigate to="/agents" replace /> },
      { path: "agents", element: <AgentListPage /> },
      { path: "agents/new", element: <AgentCreatePage /> },
      { path: "agents/search", element: <AgentSearchPage /> },
      { path: "agents/:id", element: <AgentDetailPage /> },
      { path: "providers", element: <ProviderListPage /> },
      { path: "providers/new", element: <ProviderCreatePage /> },
      { path: "providers/:id", element: <ProviderDetailPage /> },
      { path: "*", element: <NotFoundPage /> },
    ],
  },
]);

function App() {
  return <RouterProvider router={router} />;
}

export default App;
