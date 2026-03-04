/**
 * User Signup Page
 */

import React, { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import {
  HiUser,
  HiEnvelope,
  HiPhone,
  HiLockClosed,
  HiEye,
  HiEyeSlash,
  HiVideoCamera,
  HiMapPin,
  HiSignal,
  HiWifi,
  HiArrowLeft,
} from 'react-icons/hi2';
import { AuthAPI } from '@/services/api';
import { isValidEmail, isValidPhone } from '@/utils/helpers';
import { useDocumentTitle } from '@/hooks';
import toast from 'react-hot-toast';

export default function UserSignupPage() {
  useDocumentTitle('User Signup');
  const navigate = useNavigate();

  const [form, setForm] = useState({
    name: '',
    email: '',
    phone: '',
    password: '',
    confirmPassword: '',
    cctvName: '',
    location: '',
    cameraIp: '',
    cameraUsername: '',
    cameraPassword: '',
    cameraPort: '',
    cameraBrand: '',
  });
  const [loading, setLoading] = useState(false);
  const [pwVisible, setPwVisible] = useState(false);

  const set = (field: string, value: string) =>
    setForm((prev) => ({ ...prev, [field]: value }));

  const handleSignup = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!form.name || !form.email || !form.phone || !form.password) {
      toast.error('Please fill in all required personal fields');
      return;
    }
    if (!isValidEmail(form.email)) {
      toast.error('Invalid email address');
      return;
    }
    if (!isValidPhone(form.phone)) {
      toast.error('Invalid phone number');
      return;
    }
    if (form.password.length < 6) {
      toast.error('Password must be at least 6 characters');
      return;
    }
    if (form.password !== form.confirmPassword) {
      toast.error('Passwords do not match');
      return;
    }
    if (!form.cctvName || !form.location) {
      toast.error('Please provide CCTV name and location');
      return;
    }
    if (!form.cameraIp || !form.cameraUsername || !form.cameraPassword) {
      toast.error('Please provide camera IP, username and password');
      return;
    }

    setLoading(true);
    try {
      const result = await AuthAPI.signupUser({
        name: form.name,
        email: form.email,
        phone: form.phone,
        password: form.password,
        cctvName: form.cctvName,
        location: form.location,
        cameraIp: form.cameraIp,
        cameraUsername: form.cameraUsername,
        cameraPassword: form.cameraPassword,
        cameraPort: form.cameraPort || undefined,
        cameraBrand: form.cameraBrand || undefined,
      });

      if (result.success) {
        toast.success('Account created! Please login.');
        navigate('/login');
      } else {
        toast.error(result.error || 'Signup failed');
      }
    } catch {
      toast.error('Network error');
    } finally {
      setLoading(false);
    }
  };

  const inputRow = (
    icon: React.ReactNode,
    placeholder: string,
    field: string,
    type = 'text',
    extra?: React.ReactNode,
  ) => (
    <div className="relative">
      <span className="absolute left-4 top-1/2 -translate-y-1/2 text-slate-400">
        {icon}
      </span>
      <input
        type={type}
        placeholder={placeholder}
        className="input-field pl-11 pr-11"
        value={(form as Record<string, string>)[field]}
        onChange={(e) => set(field, e.target.value)}
        autoComplete="off"
      />
      {extra && (
        <span className="absolute right-4 top-1/2 -translate-y-1/2">
          {extra}
        </span>
      )}
    </div>
  );

  return (
    <div className="min-h-screen bg-light-bg dark:bg-dark-bg py-10 px-4 transition-colors duration-200">
      <div className="max-w-lg mx-auto">
        <button
          onClick={() => navigate(-1)}
          className="flex items-center gap-2 text-slate-500 dark:text-slate-400 hover:text-slate-800 dark:hover:text-white mb-6"
        >
          <HiArrowLeft size={20} /> Back
        </button>

        <h1 className="text-3xl font-bold text-slate-800 dark:text-white mb-2">User Registration</h1>
        <p className="text-slate-400 mb-8">Create your account to get started</p>

        <form onSubmit={handleSignup} className="space-y-4">
          {/* Personal */}
          <h3 className="text-primary font-semibold text-sm uppercase tracking-wide">
            Personal Information
          </h3>
          {inputRow(<HiUser size={18} />, 'Full Name *', 'name')}
          {inputRow(<HiEnvelope size={18} />, 'Email Address *', 'email', 'email')}
          {inputRow(<HiPhone size={18} />, 'Phone Number *', 'phone', 'tel')}
          {inputRow(
            <HiLockClosed size={18} />,
            'Password *',
            'password',
            pwVisible ? 'text' : 'password',
            <button
              type="button"
              onClick={() => setPwVisible(!pwVisible)}
              className="text-slate-400 hover:text-slate-200"
            >
              {pwVisible ? <HiEyeSlash size={18} /> : <HiEye size={18} />}
            </button>,
          )}
          {inputRow(
            <HiLockClosed size={18} />,
            'Confirm Password *',
            'confirmPassword',
            pwVisible ? 'text' : 'password',
          )}

          {/* CCTV */}
          <h3 className="text-primary font-semibold text-sm uppercase tracking-wide pt-2">
            CCTV Configuration
          </h3>
          {inputRow(<HiVideoCamera size={18} />, 'CCTV Name *', 'cctvName')}
          {inputRow(<HiSignal size={18} />, 'Camera IP Address *', 'cameraIp')}
          {inputRow(<HiUser size={18} />, 'Camera Username *', 'cameraUsername')}
          {inputRow(
            <HiLockClosed size={18} />,
            'Camera Password *',
            'cameraPassword',
            'password',
          )}
          {inputRow(<HiWifi size={18} />, 'Port (optional, default 554)', 'cameraPort')}
          {inputRow(<HiVideoCamera size={18} />, 'Brand (optional)', 'cameraBrand')}
          {inputRow(<HiMapPin size={18} />, 'Location *', 'location')}

          <button type="submit" disabled={loading} className="btn-accent w-full py-3.5 text-lg mt-2">
            {loading ? (
              <span className="inline-block w-5 h-5 border-2 border-white border-t-transparent rounded-full animate-spin" />
            ) : (
              'Create Account'
            )}
          </button>
        </form>

        <p className="text-center mt-5 text-sm text-slate-400">
          Already have an account?{' '}
          <Link to="/login" className="text-accent hover:underline">
            Login
          </Link>
        </p>
      </div>
    </div>
  );
}
