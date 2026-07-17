import React from 'react'
import ReactDOM from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter } from 'react-router-dom'
import { ConfigProvider } from 'antd'
import zhCN from 'antd/locale/zh_CN'
import App from './App'
import './styles.css'

const queryClient = new QueryClient({
  defaultOptions: { queries: { staleTime: 30_000, retry: 1, refetchOnWindowFocus: false } },
})

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <ConfigProvider locale={zhCN} theme={{
      token: { colorPrimary: '#087F5B', colorInfo: '#087F5B', colorSuccess: '#16803A', colorWarning: '#B86B00', colorError: '#C93C4E', colorText: '#20262E', colorTextSecondary: '#58635E', colorBorder: '#D7DEDA', borderRadius: 8, fontFamily: 'Inter, "Source Han Sans SC", "Microsoft YaHei", sans-serif' },
      components: { Layout: { bodyBg: '#F5F7F6', headerBg: '#FFFFFF', siderBg: '#FFFFFF' }, Card: { borderRadiusLG: 8 } },
    }}>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter><App /></BrowserRouter>
      </QueryClientProvider>
    </ConfigProvider>
  </React.StrictMode>,
)
