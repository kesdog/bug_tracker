import React, { useState } from 'react';
import { AuthLayout, CredentialRecoveryForm } from '../components/AuthViews';
import { readDemoConfig } from '../demo_config';
import { getAppHeaderMeta } from '../appViewConfig';
import { useI18n } from '../i18n';
import { useAuth } from '../providers/AuthProvider';
import { useSession } from '../providers/SessionProvider';

export default function CredentialRecoveryPage() {
  const { t } = useI18n();
  const { loading, error, successMessage, showView, submitRecoveryRequest } = useAuth();
  const { isRestoring } = useSession();
  const [demoConfig] = useState(readDemoConfig);
  const [requestType, setRequestType] = useState('human');
  const [email, setEmail] = useState('');
  const [confirmEmail, setConfirmEmail] = useState('');
  const headerMeta = getAppHeaderMeta({ session: null, isSetupRoute: false, loginView: 'recovery', currentPage: 'dashboard', t });

  async function handleSubmit(event) {
    event.preventDefault();
    const submitted = await submitRecoveryRequest(email, confirmEmail, requestType);
    if (submitted) {
      setEmail('');
      setConfirmEmail('');
    }
  }

  return (
    <AuthLayout title={headerMeta.title} description={headerMeta.description} error={error} successMessage={successMessage} loading={loading || isRestoring}>
      <CredentialRecoveryForm
        requestType={requestType}
        email={email}
        confirmEmail={confirmEmail}
        loading={loading}
        isDemo={Boolean(demoConfig)}
        onTypeChange={setRequestType}
        onEmailChange={setEmail}
        onConfirmEmailChange={setConfirmEmail}
        onSubmit={handleSubmit}
        onBack={() => showView('login')}
      />
    </AuthLayout>
  );
}
