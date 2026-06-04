import { createBrowserRouter } from "react-router-dom";
import { AppShell } from "../components/AppShell";
import { Dashboard } from "../routes/Dashboard";
import { NewMigrationRedirect, WizardShell } from "../wizard/WizardShell";

export const router = createBrowserRouter([
  {
    path: "/",
    element: <AppShell />,
    children: [
      { index: true, element: <Dashboard /> },
      { path: "migrations/new", element: <NewMigrationRedirect /> },
      {
        path: "migrations/:id",
        element: <WizardShell />,
        children: [
          // step routes added in Tasks 5-10
        ],
      },
    ],
  },
]);
