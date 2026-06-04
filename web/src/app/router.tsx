import { createBrowserRouter } from "react-router-dom";
import { AppShell } from "../components/AppShell";
import { Dashboard } from "../routes/Dashboard";
import { NewMigrationRedirect, WizardShell } from "../wizard/WizardShell";
import { StepFromTo } from "../wizard/StepFromTo";
import { StepConnect } from "../wizard/StepConnect";

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
          { path: "from-to", element: <StepFromTo /> },
          { path: "connect/:side", element: <StepConnect /> },
          // step routes added in Tasks 7-10
        ],
      },
    ],
  },
]);
