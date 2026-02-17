import { createBrowserRouter, Navigate, RouterProvider } from "react-router";
import { AppLayout } from "@/components/layout/AppLayout";
import AgentListPage from "@/pages/AgentListPage";
import AgentCreatePage from "@/pages/AgentCreatePage";
import AgentDetailPage from "@/pages/AgentDetailPage";
import AgentSearchPage from "@/pages/AgentSearchPage";
import ProviderListPage from "@/pages/ProviderListPage";
import ProviderCreatePage from "@/pages/ProviderCreatePage";
import ProviderDetailPage from "@/pages/ProviderDetailPage";
import ToolListPage from "@/pages/ToolListPage";
import ToolCreatePage from "@/pages/ToolCreatePage";
import ToolDetailPage from "@/pages/ToolDetailPage";
import OpenApiImportPage from "@/pages/OpenApiImportPage";
import ChatPage from "@/pages/ChatPage";
import WorkflowListPage from "@/pages/WorkflowListPage";
import WorkflowCreatePage from "@/pages/WorkflowCreatePage";
import WorkflowDetailPage from "@/pages/WorkflowDetailPage";
import WorkflowExecutionDetailPage from "@/pages/WorkflowExecutionDetailPage";
import SandboxListPage from "@/pages/SandboxListPage";
import SandboxCreatePage from "@/pages/SandboxCreatePage";
import SandboxDetailPage from "@/pages/SandboxDetailPage";
import SkillListPage from "@/pages/SkillListPage";
import SkillCreatePage from "@/pages/SkillCreatePage";
import SkillDetailPage from "@/pages/SkillDetailPage";
import DataSourceListPage from "@/pages/DataSourceListPage";
import DataSourceCreatePage from "@/pages/DataSourceCreatePage";
import DataSourceDetailPage from "@/pages/DataSourceDetailPage";
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
      { path: "tools", element: <ToolListPage /> },
      { path: "tools/new", element: <ToolCreatePage /> },
      { path: "tools/import", element: <OpenApiImportPage /> },
      { path: "tools/:id", element: <ToolDetailPage /> },
      { path: "workflows", element: <WorkflowListPage /> },
      { path: "workflows/new", element: <WorkflowCreatePage /> },
      { path: "workflows/:id", element: <WorkflowDetailPage /> },
      { path: "workflows/:id/executions/:execId", element: <WorkflowExecutionDetailPage /> },
      { path: "sandboxes", element: <SandboxListPage /> },
      { path: "sandboxes/new", element: <SandboxCreatePage /> },
      { path: "sandboxes/:id", element: <SandboxDetailPage /> },
      { path: "skills", element: <SkillListPage /> },
      { path: "skills/new", element: <SkillCreatePage /> },
      { path: "skills/:id", element: <SkillDetailPage /> },
      { path: "datasources", element: <DataSourceListPage /> },
      { path: "datasources/new", element: <DataSourceCreatePage /> },
      { path: "datasources/:id", element: <DataSourceDetailPage /> },
      { path: "chat", element: <ChatPage /> },
      { path: "*", element: <NotFoundPage /> },
    ],
  },
]);

function App() {
  return <RouterProvider router={router} />;
}

export default App;
