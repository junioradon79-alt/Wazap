import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { api, getToken, getUser, setToken, setUser, type StoredUser } from '../api/client'
import type { AuthResponse } from '../api/types'

interface AuthContextValue {
  user: StoredUser | null
  loading: boolean
  login: (username: string, password: string) => Promise<void>
  logout: () => void
}

const AuthContext = createContext<AuthContextValue>(null!)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUserState] = useState<StoredUser | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    const handleUnauthorized = () => setUserState(null)
    window.addEventListener('wazap:unauthorized', handleUnauthorized)

    // Restauration de session au chargement
    if (getToken() && getUser()) {
      setUserState(getUser())
    }
    setLoading(false)

    return () => window.removeEventListener('wazap:unauthorized', handleUnauthorized)
  }, [])

  const login = async (username: string, password: string): Promise<void> => {
    const res = await api.post<AuthResponse>('/auth/login', { username, password })
    const stored: StoredUser = { userId: res.userId, username: res.username, role: res.role }
    setToken(res.token)
    setUser(stored)
    setUserState(stored)
  }

  const logout = (): void => {
    setToken(null)
    setUser(null)
    setUserState(null)
  }

  return (
    <AuthContext.Provider value={{ user, loading, login, logout }}>
      {children}
    </AuthContext.Provider>
  )
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthContextValue {
  return useContext(AuthContext)
}
