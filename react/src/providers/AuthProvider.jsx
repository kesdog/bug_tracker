import React, { createContext, useContext, useEffect, useState } from 'react';
import { login, requestAccess, requestCredentialRecovery, setupPassword } from '../api/auth';
import { initializeSessionActivity, SESSION_INACTIVITY_TIMEOUT_MS, SESSION_TOKEN_KEY } from '../session_manager';
import { useSession } from './SessionProvider';

const AuthContext = createContext(null);

export function AuthProvider({ children }) {
  const { startSession, sessionEndReason } = useSession();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [successMessage, setSuccessMessage] = useState('');
  const [view, setView] = useState('login');
  const [prefilledLoginEmail, setPrefilledLoginEmail] = useState('');

  useEffect(() => {
    if (sessionEndReason === 'inactive') {
      setError(`You were signed out after ${Math.round(SESSION_INACTIVITY_TIMEOUT_MS / 60_000)} minutes of inactivity.`);
    } else if (sessionEndReason === 'unauthorized') {
      setError('Your session expired or was revoked. Please sign in again.');
    } else if (sessionEndReason) {
      setError('');
    }
  }, [sessionEndReason]);

  function clearFeedback() {
    setError('');
    setSuccessMessage('');
  }

  function showView(nextView) {
    clearFeedback();
    setView(nextView);
  }

  function prepareSignIn(email = '') {
    setPrefilledLoginEmail(email);
    showView('login');
  }

  async function signIn(email, password) {
    clearFeedback();
    if (!email.trim() || !password.trim()) {
      setError('Email and password are required.');
      return false;
    }

    setLoading(true);
    try {
      const result = await login(email.trim(), password);
      localStorage.setItem(SESSION_TOKEN_KEY, result.accessToken);
      initializeSessionActivity();
      await startSession(result.accessToken, result.user);
      return true;
    } catch (err) {
      setError(err.message || 'Login failed.');
      return false;
    } finally {
      setLoading(false);
    }
  }

  async function submitAccessRequest(email, confirmationEmail, requestType, isDemo) {
    clearFeedback();
    const emailValue = email.trim().toLowerCase();
    const confirmation = confirmationEmail.trim().toLowerCase();
    if (!emailValue || !confirmation) {
      setError('Email and confirm email are required.');
      return false;
    }
    if (emailValue !== confirmation) {
      setError('Email and confirm email must match.');
      return false;
    }

    setLoading(true);
    try {
      await requestAccess(emailValue, requestType);
      setSuccessMessage(isDemo
        ? 'Access request submitted for demo review. No email will be sent.'
        : 'Access request submitted. Admin will review your request.');
      setView('login');
      return true;
    } catch (err) {
      setError(err.message || 'Unable to submit access request.');
      return false;
    } finally {
      setLoading(false);
    }
  }

  async function submitRecoveryRequest(email, confirmationEmail, requestType) {
    clearFeedback();
    const emailValue = email.trim().toLowerCase();
    const confirmation = confirmationEmail.trim().toLowerCase();
    if (!emailValue || !confirmation) {
      setError('Email and confirm email are required.');
      return false;
    }
    if (emailValue !== confirmation) {
      setError('Email and confirm email must match.');
      return false;
    }

    setLoading(true);
    try {
      const result = await requestCredentialRecovery(emailValue, requestType);
      setSuccessMessage(result.message || 'If the account exists, an administrator can review the request.');
      setView('login');
      return true;
    } catch (err) {
      setError(err.message || 'Unable to submit credential recovery request.');
      return false;
    } finally {
      setLoading(false);
    }
  }

  async function submitPasswordSetup(email, confirmationEmail, token, password, confirmationPassword) {
    clearFeedback();
    const emailValue = email.trim().toLowerCase();
    const confirmation = confirmationEmail.trim().toLowerCase();
    if (!emailValue || !confirmation || !password || !confirmationPassword) {
      setError('All setup fields are required.');
      return false;
    }
    if (!token) {
      setError('Setup token is missing from link.');
      return false;
    }
    if (emailValue !== confirmation) {
      setError('Email and confirmation email must match.');
      return false;
    }
    if (password !== confirmationPassword) {
      setError('New password and confirmation must match.');
      return false;
    }
    if (!/[0-9]/.test(password) || !/[^A-Za-z0-9]/.test(password) || password.length < 12) {
      setError('Password must be at least 12 characters with one number and one special character.');
      return false;
    }

    setLoading(true);
    try {
      await setupPassword(emailValue, token, password);
      setSuccessMessage('Password set successfully. You can now sign in.');
      return true;
    } catch (err) {
      setError(err.message || 'Unable to complete user setup.');
      return false;
    } finally {
      setLoading(false);
    }
  }

  return (
    <AuthContext.Provider value={{
      loading,
      error,
      successMessage,
      view,
      prefilledLoginEmail,
      clearFeedback,
      showView,
      prepareSignIn,
      signIn,
      submitAccessRequest,
      submitRecoveryRequest,
      submitPasswordSetup
    }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used within AuthProvider.');
  return context;
}
