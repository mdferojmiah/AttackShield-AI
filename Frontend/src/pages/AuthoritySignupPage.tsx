/**
 * Authority Signup Page
 */

import React, { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import {
  HiUser,
  HiEnvelope,
  HiIdentification,
  HiBuildingOffice2,
  HiLockClosed,
  HiEye,
  HiEyeSlash,
  HiArrowLeft,
} from 'react-icons/hi2';
import { AuthAPI } from '@/services/api';
import { isValidEmail } from '@/utils/helpers';
import { useDocumentTitle } from '@/hooks';
import toast from 'react-hot-toast';

export default function AuthoritySignupPage() {
  useDocumentTitle('Authority Signup');
  const navigate = useNavigate();

  const [form, setForm] = useState({
    name: '',
    email: '',
    officerId: '',
    stationName: '',
    password: '',
    confirmPassword: '',
  });
  const [loading, setLoading] = useState(false);
  const [pwVisible, setPwVisible] = useState(false);

  const set = (field: string, value: string) =>
    setForm((prev) => ({ ...prev, [field]: value }));

  const handleSignup = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!form.name || !form.email || !form.officerId || !form.stationName || !form.password) {
      toast.error('Please fill in all required fields');
      return;
    }
    if (!isValidEmail(form.email)) {
      toast.error('Invalid email address');
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

    setLoading(true);
    try {
      const result = await AuthAPI.signupAuthority({
        name: form.name,
        email: form.email,
        officerId: form.officerId,
        stationName: form.stationName,
        password: form.password,
      });
      if (result.success) {
        toast.success('Authority account created! Please login.');
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

  return (
    <div className="min-h-screen bg-light-bg dark:bg-dark-bg py-10 px-4 transition-colors duration-200">
      <div className="max-w-lg mx-auto">
        <button
          onClick={() => navigate(-1)}
          className="flex items-center gap-2 text-slate-500 dark:text-slate-400 hover:text-slate-800 dark:hover:text-white mb-6"
        >
          <HiArrowLeft size={20} /> Back
        </button>

        <h1 className="text-3xl font-bold text-slate-800 dark:text-white mb-2">Authority Registration</h1>
        <p className="text-slate-400 mb-8">Register as a law enforcement authority</p>

        <form onSubmit={handleSignup} className="space-y-4">
          {/* Name */}
          <div className="relative">
            <HiUser className="absolute left-4 top-1/2 -translate-y-1/2 text-slate-400" size={18} />
            <input type="text" placeholder="Full Name *" className="input-field pl-11" value={form.name} onChange={(e) => set('name', e.target.value)} />
          </div>

          {/* Email */}
          <div className="relative">
            <HiEnvelope className="absolute left-4 top-1/2 -translate-y-1/2 text-slate-400" size={18} />
            <input type="email" placeholder="Official Email *" className="input-field pl-11" value={form.email} onChange={(e) => set('email', e.target.value)} />
          </div>

          {/* Officer ID */}
          <div className="relative">
            <HiIdentification className="absolute left-4 top-1/2 -translate-y-1/2 text-slate-400" size={18} />
            <input type="text" placeholder="Officer ID *" className="input-field pl-11" value={form.officerId} onChange={(e) => set('officerId', e.target.value)} />
          </div>

          {/* Station Name */}
          <div className="relative">
            <HiBuildingOffice2 className="absolute left-4 top-1/2 -translate-y-1/2 text-slate-400" size={18} />
            <input type="text" placeholder="Station Name *" className="input-field pl-11" value={form.stationName} onChange={(e) => set('stationName', e.target.value)} />
          </div>

          {/* Password */}
          <div className="relative">
            <HiLockClosed className="absolute left-4 top-1/2 -translate-y-1/2 text-slate-400" size={18} />
            <input
              type={pwVisible ? 'text' : 'password'}
              placeholder="Password *"
              className="input-field pl-11 pr-11"
              value={form.password}
              onChange={(e) => set('password', e.target.value)}
            />
            <button
              type="button"
              onClick={() => setPwVisible(!pwVisible)}
              className="absolute right-4 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-200"
            >
              {pwVisible ? <HiEyeSlash size={18} /> : <HiEye size={18} />}
            </button>
          </div>

          {/* Confirm Password */}
          <div className="relative">
            <HiLockClosed className="absolute left-4 top-1/2 -translate-y-1/2 text-slate-400" size={18} />
            <input
              type={pwVisible ? 'text' : 'password'}
              placeholder="Confirm Password *"
              className="input-field pl-11"
              value={form.confirmPassword}
              onChange={(e) => set('confirmPassword', e.target.value)}
            />
          </div>

          <button type="submit" disabled={loading} className="btn-accent w-full py-3.5 text-lg">
            {loading ? (
              <span className="inline-block w-5 h-5 border-2 border-white border-t-transparent rounded-full animate-spin" />
            ) : (
              'Register as Authority'
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
