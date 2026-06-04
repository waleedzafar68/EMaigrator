import { RouterProvider } from "react-router-dom";
import { ThemeProvider } from "./app/ThemeProvider";
import { router } from "./app/router";

export default function App() {
  return (
    <ThemeProvider>
      <RouterProvider router={router} />
    </ThemeProvider>
  );
}
