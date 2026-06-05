import { createBrowserRouter } from "react-router-dom";
import { setUnauthorizedHandler } from "../api/client";
import { AppShell } from "../components/AppShell";
import { Dashboard } from "../routes/Dashboard";
import { Login } from "../routes/Login";
import { Results } from "../routes/Results";
import { NewMigrationRedirect, WizardShell } from "../wizard/WizardShell";
import { StepFromTo } from "../wizard/StepFromTo";
import { StepConnect } from "../wizard/StepConnect";
import { StepScope } from "../wizard/StepScope";
import { StepReview } from "../wizard/StepReview";
import { StepRun } from "../wizard/StepRun";

export const router = createBrowserRouter([
  { path: "/login", element: <Login /> },
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
        ],
      },
      { path: "migrations/:id/results", element: <Results /> },
    ],
  },
]);

// Any API 401 sends the user to the login page (unless they're already there — e.g. a failed login).
setUnauthorizedHandler(() => {
  if (router.state.location.pathname !== "/login") {
    void router.navigate("/login");
  }
});
