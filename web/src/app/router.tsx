import { createBrowserRouter } from "react-router-dom";
import { AppShell } from "../components/AppShell";
import { Dashboard } from "../routes/Dashboard";
import { NewMigrationRedirect, WizardShell } from "../wizard/WizardShell";
import { StepFromTo } from "../wizard/StepFromTo";
import { StepConnect } from "../wizard/StepConnect";
import { StepScope } from "../wizard/StepScope";
import { StepReview } from "../wizard/StepReview";
import { StepRun } from "../wizard/StepRun";

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
          { path: "scope", element: <StepScope /> },
          { path: "review", element: <StepReview /> },
          { path: "run", element: <StepRun /> },
          // step routes added in Task 10
        ],
      },
    ],
  },
]);
