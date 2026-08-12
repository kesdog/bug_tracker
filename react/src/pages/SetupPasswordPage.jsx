import React, { useEffect, useState } from 'react';
import { AuthLayout, SetupPasswordForm } from '../components/AuthViews';
import { getAppHeaderMeta } from '../appViewConfig';
import { useI18n } from '../i18n';
import { useAuth } from '../providers/AuthProvider';
import { useSession } from '../providers/SessionProvider';

export default function SetupPasswordPage() {
  const { t } = useI18n();
  const { loading, error, successMessage, prepareSignIn, submitPasswordSetup } = useAuth();
  const { isRestoring } = useSession();
  const searchParams = new URLSearchParams(window.location.search);
  const token = searchParams.get('token') || '';
  const linkedEmail = (searchParams.get('email') || '').toLowerCase();
  const [email, setEmail] = useState(linkedEmail);
  const [confirmEmail, setConfirmEmail] = useState(linkedEmail);
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const headerMeta = getAppHeaderMeta({ session: null, isSetupRoute: true, loginView: 'login', currentPage: 'dashboard', t });

  useEffect(() => {
    setEmail(linkedEmail);
    setConfirmEmail(linkedEmail);
  }, [linkedEmail]);

  async function handleSubmit(event) {
    event.preventDefault();
    const complete = await submitPasswordSetup(email, confirmEmail, token, password, confirmPassword);
    if (complete) {
      prepareSignIn(email.trim().toLowerCase());
      window.history.replaceState({}, '', '/');
      window.dispatchEvent(new PopStateEvent('popstate'));
      setEmail('');
      setConfirmEmail('');
      setPassword('');
      setConfirmPassword('');
    }
  }

  return (
    <AuthLayout title={headerMeta.title} description={headerMeta.description} error={error} successMessage={successMessage} loading={loading || isRestoring}>
      <SetupPasswordForm
        email={email}
        confirmEmail={confirmEmail}
        password={password}
        confirmPassword={confirmPassword}
        loading={loading}
        onEmailChange={setEmail}
        onConfirmEmailChange={setConfirmEmail}
        onPasswordChange={setPassword}
        onConfirmPasswordChange={setConfirmPassword}
        onSubmit={handleSubmit}
      />
    </AuthLayout>
  );
}
