import { createBrowserRouter } from "react-router-dom";
import { AppShell } from "../components/AppShell";
import { Dashboard } from "../routes/Dashboard";

export const router = createBrowserRouter([
  {
    path: "/",
    element: <AppShell />,
    children: [
      { index: true, element: <Dashboard /> },
      // wizard + run + results routes registered in later tasks
    ],
  },
]);
