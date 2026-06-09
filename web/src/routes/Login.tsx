import { useState, type FormEvent, type ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { Eye, EyeOff, Mailbox } from "lucide-react";
import { ApiError } from "../api/client";
import { login, register } from "../api/auth";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "../components/ui/card";

type Mode = "login" | "register";

const MIN_PASSWORD = 12;

export function Login() {
  const navigate = useNavigate();
  const [mode, setMode] = useState<Mode>("login");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [organizationName, setOrganizationName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [showPassword, setShowPassword] = useState(false);

  const isRegister = mode === "register";

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);

    if (isRegister && password.length < MIN_PASSWORD) {
      setError(`Password must be at least ${MIN_PASSWORD} characters.`);
      return;
    }

    setBusy(true);
    try {
      // Register creates the account but does not sign in, so always finish with a login.
      if (isRegister) {
        await register({ email, password, organizationName });
      }
      await login({ email, password });
      navigate("/", { replace: true });
    } catch (err) {
      setError(messageFor(err, isRegister));
    } finally {
      setBusy(false);
    }
  }

  function toggleMode() {
    setMode(isRegister ? "login" : "register");
    setError(null);
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-bg p-4 text-fg">
      <Card className="w-full max-w-[400px]">
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-[length:var(--fs-h2)]">
            <span className="flex h-8 w-8 items-center justify-center rounded-md bg-accent text-accent-fg">
              <Mailbox size={18} aria-hidden />
            </span>
            EMaigrator
          </CardTitle>
          <CardDescription>
            {isRegister
              ? "Create your account to start migrating mailboxes."
              : "Sign in to continue."}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={onSubmit} className="space-y-4" noValidate>
            {isRegister && (
              <Field id="org" label="Organization">
                <Input
                  id="org"
                  autoComplete="organization"
                  required
                  value={organizationName}
                  onChange={(e) => setOrganizationName(e.target.value)}
                />
              </Field>
            )}
            <Field id="email" label="Email">
              <Input
                id="email"
                type="email"
                autoComplete="email"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
              />
            </Field>
            <Field
              id="password"
              label="Password"
              hint={isRegister ? `At least ${MIN_PASSWORD} characters.` : undefined}
            >
              <div className="relative">
                <Input
                  id="password"
                  type={showPassword ? "text" : "password"}
                  autoComplete={isRegister ? "new-password" : "current-password"}
                  required
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  className="pr-10"
                />
                <button
                  type="button"
                  aria-label={showPassword ? "Hide value" : "Show value"}
                  onClick={() => setShowPassword((v) => !v)}
                  className="absolute inset-y-0 right-0 flex w-10 items-center justify-center text-fg-muted hover:text-fg"
                >
                  {showPassword ? <EyeOff size={16} aria-hidden /> : <Eye size={16} aria-hidden />}
                </button>
              </div>
            </Field>
            {error && (
              <p role="alert" className="text-sm text-destructive">
                {error}
              </p>
            )}
            <Button type="submit" className="w-full" disabled={busy}>
              {busy ? "Please wait…" : isRegister ? "Create account" : "Sign in"}
            </Button>
          </form>
          <p className="mt-4 text-center text-sm text-fg-muted">
            {isRegister ? "Already have an account? " : "Need an account? "}
            <button
              type="button"
              onClick={toggleMode}
              className="text-accent underline-offset-4 hover:underline"
            >
              {isRegister ? "Sign in" : "Create one"}
            </button>
          </p>
        </CardContent>
      </Card>
    </div>
  );
}

function Field({
  id,
  label,
  hint,
  children,
}: {
  id: string;
  label: string;
  hint?: string;
  children: ReactNode;
}) {
  return (
    <div className="space-y-1.5">
      <label htmlFor={id} className="text-sm font-medium">
        {label}
      </label>
      {children}
      {hint && <p className="text-xs text-fg-muted">{hint}</p>}
    </div>
  );
}

function messageFor(err: unknown, isRegister: boolean): string {
  if (err instanceof ApiError) {
    if (err.status === 401) return "Incorrect email or password.";
    if (isRegister && err.status === 400) {
      return "Could not create the account — check the email and use a password of at least 12 characters.";
    }
    if (err.status === 409) return "An account with that email already exists.";
    return err.message;
  }
  return "Something went wrong. Please try again.";
}
