import React, { useState } from 'react';
import { AuthLayout, DemoLoginPanel, LoginForm, RequestAccessForm } from '../components/AuthViews';
import { readDemoConfig } from '../demo_config';
import { getAppHeaderMeta } from '../appViewConfig';
import { useI18n } from '../i18n';
import { useAuth } from '../providers/AuthProvider';
import { useSession } from '../providers/SessionProvider';

export default function LoginPage() {
  const { t } = useI18n();
  const { loading, error, successMessage, view, prefilledLoginEmail, showView, signIn, submitAccessRequest } = useAuth();
  const { isRestoring } = useSession();
  const [demoConfig] = useState(readDemoConfig);
  const [email, setEmail] = useState(prefilledLoginEmail);
  const [password, setPassword] = useState('');
  const [requestType, setRequestType] = useState('human');
  const [requestEmail, setRequestEmail] = useState('');
  const [requestEmailConfirm, setRequestEmailConfirm] = useState('');
  const headerMeta = getAppHeaderMeta({ session: null, isSetupRoute: false, loginView: view, currentPage: 'dashboard', t });

  async function handleLogin(event) {
    event.preventDefault();
    const signedIn = await signIn(email, password);
    if (signedIn) setPassword('');
  }

  async function handleAccessRequest(event) {
    event.preventDefault();
    const submitted = await submitAccessRequest(requestEmail, requestEmailConfirm, requestType, Boolean(demoConfig));
    if (submitted) {
      setRequestEmail('');
      setRequestEmailConfirm('');
    }
  }

  return (
    <AuthLayout title={headerMeta.title} description={headerMeta.description} error={error} successMessage={successMessage} loading={loading || isRestoring}>
      {view === 'request' ? (
        <RequestAccessForm
          requestType={requestType}
          email={requestEmail}
          confirmEmail={requestEmailConfirm}
          loading={loading}
          isDemo={Boolean(demoConfig)}
          onTypeChange={setRequestType}
          onEmailChange={setRequestEmail}
          onConfirmEmailChange={setRequestEmailConfirm}
          onSubmit={handleAccessRequest}
          onBack={() => showView('login')}
        />
      ) : (
        <>
          <LoginForm
            email={email}
            password={password}
            loading={loading}
            onEmailChange={setEmail}
            onPasswordChange={setPassword}
            onSubmit={handleLogin}
            onRequestAccess={() => showView('request')}
            onRecoverCredentials={() => showView('recovery')}
          />
          <DemoLoginPanel config={demoConfig} onSelect={(account) => { setEmail(account.email); setPassword(account.password); }} />
        </>
      )}
    </AuthLayout>
  );
}
