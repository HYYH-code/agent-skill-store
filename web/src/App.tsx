import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, Navigate, Route, Routes, useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import {
  ApiOutlined, AppstoreOutlined, AuditOutlined, CheckCircleFilled, CheckOutlined, CloudServerOutlined,
  CodeOutlined, CopyOutlined, DeleteOutlined, DeploymentUnitOutlined, FileMarkdownOutlined,
  FileSearchOutlined, HomeOutlined, InboxOutlined, KeyOutlined, LogoutOutlined,
  MenuFoldOutlined, MenuOutlined, MenuUnfoldOutlined, PlusOutlined, ReloadOutlined, SafetyCertificateOutlined,
  SearchOutlined, SettingOutlined, StopOutlined, TeamOutlined, UploadOutlined, UserOutlined,
} from '@ant-design/icons'
import {
  Alert, Avatar, Badge, Breadcrumb, Button, Card, Checkbox, Col, Descriptions, Divider, Drawer, Dropdown, Empty, Form,
  Input, Layout, List, Menu, message, Modal, Pagination, Progress, Result, Row, Segmented, Select, Skeleton,
  Space, Statistic, Steps, Table, Tabs, Tag, Timeline, Typography, Upload,
} from 'antd'
import type { MenuProps, TableProps } from 'antd'
import ReactMarkdown from 'react-markdown'
import { api } from './api'
import { LogoLockup, LogoMark } from './brand'
import { cliAgentTargets, createCliCommand, createCliCommandSet, createSkillReference, type CliAction, type CliAgentId } from './cliCommands'
import { roleLabels, statusLabels } from './demo'
import { createPermissionConfirmations, hasConfirmedAll, requiresPermissionConfirmation } from './reviewPolicy'
import type { AuditEvent, PermissionSet, ReviewItem, RiskLevel, SessionUser, SkillStatus, SkillSummary } from './types'

const { Header, Sider, Content } = Layout
const { Title, Paragraph, Text } = Typography

const statusColor: Record<SkillStatus, string> = { draft: 'default', pending: 'gold', beta: 'orange', stable: 'green', deprecated: 'red', revoked: 'red' }
function StatusTag({ status }: { status: SkillStatus }) {
  const icon = status === 'stable' ? <CheckCircleFilled /> : status === 'revoked' ? <StopOutlined /> : undefined
  return <Tag color={statusColor[status]} icon={icon}>{statusLabels[status]}</Tag>
}

function RiskTag({ risk }: { risk: RiskLevel }) {
  const map = { low: ['green', '低风险'], medium: ['gold', '中风险'], high: ['volcano', '高风险'], critical: ['red', '严重风险'] } as const
  return <Tag color={map[risk][0]}>{map[risk][1]}</Tag>
}

function PermissionPanel({ permissions, compact = false }: { permissions: PermissionSet; compact?: boolean }) {
  const rows = [
    ['文件读取', permissions.filesystem.read.length ? permissions.filesystem.read.join('、') : '禁止', permissions.filesystem.read.length > 0],
    ['文件写入', permissions.filesystem.write.length ? permissions.filesystem.write.join('、') : '禁止', permissions.filesystem.write.length > 0],
    ['网络访问', permissions.network.policy === 'deny-all' ? '禁止' : `${permissions.network.policy} · ${permissions.network.allow.join('、')}`, permissions.network.policy !== 'deny-all'],
    ['系统命令', permissions.commands.length ? permissions.commands.join('、') : '禁止', permissions.commands.length > 0],
    ['密钥需求', permissions.secrets.length ? permissions.secrets.join('、') : '无', permissions.secrets.length > 0],
  ]
  return <div className={`permission-panel ${compact ? 'compact' : ''}`}>
    {rows.map(([label, value, enabled]) => <div className="permission-row" key={String(label)}>
      <span>{label}</span><span className={enabled ? 'permission-allowed' : 'permission-denied'}>{enabled ? <CheckCircleFilled /> : <StopOutlined />} {value}</span>
    </div>)}
  </div>
}

function LoadingState() { return <Card><Skeleton active paragraph={{ rows: 6 }} /></Card> }
function ErrorState({ error, retry }: { error: Error; retry?: () => void }) { return <Result status="error" title="加载失败" subTitle={error.message} extra={retry && <Button icon={<ReloadOutlined />} onClick={retry}>重试</Button>} /> }
function NoAccess() { return <Result status="403" title="没有访问权限" subTitle="当前账号没有执行此操作所需的角色，请联系平台管理员。" /> }
function LoginRequired() {
  const navigate = useNavigate()
  return <Result status="403" title="请先登录" subTitle="浏览 Skill 无需登录，发布、审核和治理操作需要后台账号。" extra={<Button type="primary" icon={<UserOutlined />} onClick={() => navigate('/login')}>登录后台</Button>} />
}

function LoginPage({ onLogin }: { onLogin: (user: SessionUser) => void }) {
  const [form] = Form.useForm()
  const [error, setError] = useState('')
  const login = useMutation({ mutationFn: ({ username, password }: { username: string; password: string }) => api.login(username, password), onSuccess: onLogin, onError: (e: Error) => setError(e.message) })
  return <div className="login-page">
    <div className="login-visual">
      <LogoLockup />
      <div className="login-copy"><span className="eyebrow">DISCOVER · GOVERN · INSTALL</span><h1>统一发现、治理与安装 Agent Skills</h1><p>公开浏览可信 Skill，通过独立审核管理版本，再由 Agent 使用 CLI 安装到本机。</p></div>
      <small>Open-source project · Apache-2.0</small>
    </div>
    <div className="login-form-wrap">
      <div className="login-form">
        <LogoLockup />
        <div className="login-title"><h2>登录 Agent Skill Store</h2><p>使用本地账号访问发布、审核和治理功能</p></div>
        {error && <Alert type="error" showIcon message={error} closable onClose={() => setError('')} />}
        <Form form={form} layout="vertical" initialValues={import.meta.env.DEV ? { username: 'admin', password: 'admin' } : undefined} onFinish={(values) => login.mutate(values)}>
          <Form.Item label="用户名" name="username" rules={[{ required: true }]}><Input prefix={<UserOutlined />} size="large" autoComplete="username" /></Form.Item>
          <Form.Item label="密码" name="password" rules={[{ required: true }]}><Input.Password prefix={<KeyOutlined />} size="large" autoComplete="current-password" /></Form.Item>
          <Button type="primary" size="large" block htmlType="submit" loading={login.isPending}>登录</Button>
        </Form>
        {import.meta.env.DEV && <Alert className="demo-hint" type="info" showIcon message="开发演示账号" description="admin/admin · author/author · reviewer/reviewer · engineer/engineer" />}
        <div className="login-footer">Open-source deployment · Apache-2.0</div>
      </div>
    </div>
  </div>
}

function AppShell({ user, onLogout }: { user: SessionUser | null; onLogout: () => void }) {
  const [collapsed, setCollapsed] = useState(false)
  const [mobileNavOpen, setMobileNavOpen] = useState(false)
  const location = useLocation()
  const navigate = useNavigate()
  const roles = new Set(user?.roles ?? [])
  const canPublish = Boolean(user && (roles.has('author') || roles.has('platform_admin')))
  const canReview = Boolean(user && (roles.has('review_admin') || roles.has('platform_admin')))
  const canAdmin = Boolean(user && roles.has('platform_admin'))
  const reviewQueue = useQuery({ queryKey: ['reviews'], queryFn: api.reviews, enabled: canReview })
  const items: MenuProps['items'] = [
    { key: '/', icon: <HomeOutlined />, label: '首页' },
    { key: '/skills', icon: <AppstoreOutlined />, label: 'Skill Catalog' },
    { key: '/cli', icon: <CodeOutlined />, label: 'Agent Skill Store CLI' },
    { type: 'divider' },
    ...(canPublish ? [{ key: '/publish', icon: <UploadOutlined />, label: '发布中心' }] : []),
    ...(canReview ? [{ key: '/reviews', icon: <SafetyCertificateOutlined />, label: '审核中心', extra: <Badge count={reviewQueue.data?.length ?? 0} size="small" /> }] : []),
    ...(canAdmin ? [{ key: '/admin', icon: <SettingOutlined />, label: '治理中心' }] : []),
    { type: 'divider' },
    { key: '/about', icon: <ApiOutlined />, label: '关于 Agent Skill Store' },
  ]
  const userMenu: MenuProps['items'] = user ? [...user.roles.map((role) => ({ key: role, label: roleLabels[role] })), { type: 'divider' }, { key: 'logout', label: '退出登录', icon: <LogoutOutlined />, danger: true }] : []
  const selected = location.pathname === '/' ? '/' : `/${location.pathname.split('/')[1]}`
  const navigateFromMenu = (key: string) => {
    navigate(key)
    setMobileNavOpen(false)
  }
  return <Layout className="app-shell">
    <Sider width={240} collapsedWidth={72} collapsed={collapsed} theme="light" className="app-sider">
      <div className="sider-brand"><LogoLockup compact={collapsed} /></div>
      <Menu mode="inline" items={items} selectedKeys={[selected]} onClick={({ key }) => navigateFromMenu(key)} />
      <button className="collapse-button" onClick={() => setCollapsed(!collapsed)}>{collapsed ? <MenuUnfoldOutlined /> : <><MenuFoldOutlined /> 收起导航</>}</button>
    </Sider>
    <Drawer
      className="mobile-nav-drawer"
      placement="left"
      width={288}
      title={<LogoLockup />}
      open={mobileNavOpen}
      onClose={() => setMobileNavOpen(false)}
    >
      <Menu mode="inline" items={items} selectedKeys={[selected]} onClick={({ key }) => navigateFromMenu(key)} />
    </Drawer>
    <Layout>
      <Header className="app-header">
        <div className="header-left">
          <Button className="mobile-menu-button" type="text" icon={<MenuOutlined />} aria-label="打开导航" title="打开导航" onClick={() => setMobileNavOpen(true)} />
          <LogoLockup className="mobile-header-logo" />
          <div className="header-brand"><span className="header-product">Agent Skill Store</span><Divider type="vertical" /><span className="header-module">Skill Catalog</span></div>
        </div>
        <Space size="middle">
          <Button className="header-cli-button" type="text" icon={<CodeOutlined />} onClick={() => navigate('/cli')}>Agent Skill Store CLI</Button>
          {user ? <Dropdown menu={{ items: userMenu, onClick: ({ key }) => key === 'logout' && onLogout() }} trigger={['click']}>
            <button className="user-button"><Avatar size={30} icon={<UserOutlined />} /><span><strong>{user.displayName}</strong><small>{user.team}</small></span></button>
          </Dropdown> : <Button icon={<UserOutlined />} onClick={() => navigate('/login')}>登录后台</Button>}
        </Space>
      </Header>
      <Content className="app-content"><Routes>
        <Route path="/" element={<HomePage user={user} />} />
        <Route path="/skills" element={<SkillsPage />} />
        <Route path="/skills/:name" element={<SkillDetailPage />} />
        <Route path="/cli" element={<CliPage />} />
        <Route path="/publish/*" element={!user ? <LoginRequired /> : canPublish ? <PublishPage /> : <NoAccess />} />
        <Route path="/reviews/*" element={!user ? <LoginRequired /> : canReview ? <ReviewsPage user={user} /> : <NoAccess />} />
        <Route path="/admin/*" element={!user ? <LoginRequired /> : canAdmin ? <AdminPage /> : <NoAccess />} />
        <Route path="/login" element={<Navigate to="/" replace />} />
        <Route path="/about" element={<AboutPage />} />
        <Route path="*" element={<Result status="404" title="页面不存在" extra={<Button type="primary" onClick={() => navigate('/')}>返回首页</Button>} />} />
      </Routes></Content>
    </Layout>
  </Layout>
}

function PageHeader({ title, subtitle, extra, crumbs }: { title: string; subtitle?: string; extra?: React.ReactNode; crumbs?: string[] }) {
  return <div className="page-heading">
    {crumbs && <Breadcrumb items={crumbs.map((title) => ({ title }))} />}
    <div><div><Title level={2}>{title}</Title>{subtitle && <Paragraph type="secondary">{subtitle}</Paragraph>}</div>{extra}</div>
  </div>
}

const cliAgentOptions = cliAgentTargets.map((target) => ({ value: target.id, label: target.label }))
const cliActionLabels: Record<CliAction, string> = { install: '安装', update: '升级', uninstall: '卸载', rollback: '回滚' }

function copyCliCommand(command: string) {
  void navigator.clipboard.writeText(command)
    .then(() => message.success('CLI 命令已复制'))
    .catch(() => message.error('复制失败，请手动选择命令'))
}

function CliCommandLine({ command }: { command: string }) {
  return <div className="cli-command-line"><Text code>{command}</Text><Button size="small" icon={<CopyOutlined />} onClick={() => copyCliCommand(command)}>复制</Button></div>
}

function CliWorkbench({ reference = '<namespace>/<skill>@<version>', compact = false }: { reference?: string; compact?: boolean }) {
  const [agent, setAgent] = useState<CliAgentId>('all')
  const [action, setAction] = useState<CliAction>('install')
  const selectedTarget = cliAgentTargets.find((target) => target.id === agent) ?? cliAgentTargets[0]
  const command = createCliCommand(action, reference, agent)
  return <Card className={`cli-workbench ${compact ? 'compact' : ''}`} title={<Space><CodeOutlined />Agent Skill Store CLI</Space>} extra={!compact && <Button type="link" onClick={() => location.assign('/skills')}>浏览工装</Button>}>
    <div className="cli-controls">
      <Select aria-label="目标 Agent" value={agent} onChange={setAgent} options={cliAgentOptions} />
      <Segmented value={action} onChange={(value) => setAction(value as CliAction)} options={Object.entries(cliActionLabels).map(([value, label]) => ({ value, label }))} />
    </div>
    <CliCommandLine command={command} />
    <Descriptions className="cli-target-meta" size="small" column={compact ? 1 : 2} items={[
      { key: 'agent', label: '目标 Agent', children: selectedTarget.label },
      { key: 'root', label: '本机目录', children: <Text code>{selectedTarget.skillRoot}</Text> },
    ]} />
  </Card>
}

function HomePage({ user }: { user: SessionUser | null }) {
  const navigate = useNavigate()
  const skills = useQuery({ queryKey: ['skills'], queryFn: api.skills })
  const stableCount = skills.data?.filter((s) => s.status === 'stable').length ?? 0
  const categoryCount = new Set(skills.data?.map((s) => s.category.id)).size
  const installableVersionCount = skills.data?.flatMap((skill) => skill.versions).filter((version) => !['draft', 'pending', 'revoked'].includes(version.status)).length ?? 0
  return <>
    <section className="home-hero">
      <div><span className="eyebrow">WELCOME · {user?.displayName ?? '访客'}</span><h1>让 Agent Skills 成为<br /><em>可信赖的工程能力</em></h1><p>统一发现、治理、版本化和安装 AI Agent Skills，减少重复建设，让经过审核的能力持续复用。</p><Space><Button type="primary" size="large" icon={<AppstoreOutlined />} onClick={() => navigate('/skills')}>浏览 Skills</Button>{user && (user.roles.includes('author') || user.roles.includes('platform_admin')) && <Button size="large" icon={<UploadOutlined />} onClick={() => navigate('/publish')}>发布新 Skill</Button>}</Space></div>
      <div className="hero-mark" aria-hidden="true"><LogoMark size={220} title="" /></div>
    </section>
    <Row gutter={20} className="metric-row">
      <Col span={6}><Card><Statistic title="已认证工装" value={stableCount} suffix="项" prefix={<SafetyCertificateOutlined />} /></Card></Col>
      <Col span={6}><Card><Statistic title="能力域" value={categoryCount} suffix="个" prefix={<DeploymentUnitOutlined />} /></Card></Col>
      <Col span={6}><Card><Statistic title="可安装版本" value={installableVersionCount} suffix="个" prefix={<AuditOutlined />} /></Card></Col>
      <Col span={6}><Card><Statistic title="CLI 目标 Agent" value={cliAgentTargets.length} suffix="类" prefix={<CloudServerOutlined />} /></Card></Col>
    </Row>
    <div className="section-title"><div><h2>推荐 Skills</h2><p>由维护团队发布，并通过独立审核后提供安装</p></div><Button type="link" onClick={() => navigate('/skills')}>查看全部</Button></div>
    {skills.isLoading ? <LoadingState /> : skills.isError ? <ErrorState error={skills.error} retry={() => skills.refetch()} /> : <Row gutter={[20, 20]}>{skills.data?.filter((s) => s.featured).map((skill) => <Col span={8} key={skill.id}><SkillCard skill={skill} /></Col>)}</Row>}
    <Row gutter={20} className="home-lower">
      <Col span={16}><Card title="最近更新" extra={<Link to="/skills">全部更新</Link>}><List dataSource={skills.data?.slice(0, 5)} renderItem={(item) => <List.Item actions={[<StatusTag key="status" status={item.status} />]}><List.Item.Meta avatar={<Avatar shape="square" className="skill-avatar"><CodeOutlined /></Avatar>} title={<Link to={`/skills/${item.name}`}>{item.displayName}</Link>} description={`${item.owner.name} · ${item.latestVersion} · ${new Date(item.updatedAt).toLocaleDateString('zh-CN')}`} /></List.Item>} /></Card></Col>
      <Col span={8}><CliWorkbench compact /></Col>
    </Row>
  </>
}

function SkillCard({ skill }: { skill: SkillSummary }) {
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  return <Card className="skill-card" hoverable onClick={() => navigate(`/skills/${skill.name}`)}>
    <div className="skill-card-top"><Avatar shape="square" size={44} className="skill-avatar"><CodeOutlined /></Avatar><StatusTag status={skill.status} /></div>
    <h3>{skill.displayName}</h3><div className="skill-id">{skill.namespace}/{skill.name}</div><p>{skill.description}</p>
    <Space wrap><Tag>{skill.category.name}</Tag><Tag>{skill.runtime[0]}</Tag><RiskTag risk={skill.riskLevel} /></Space>
    <Divider />
    <div className="skill-card-footer"><span><TeamOutlined /> {skill.owner.name}</span><Button type="primary" ghost size="small" disabled={skill.status === 'revoked'} onClick={(e) => { e.stopPropagation(); setOpen(true) }}>CLI 安装</Button></div>
    <InstallDrawer skill={skill} open={open} onClose={() => setOpen(false)} />
  </Card>
}

function SkillsPage() {
  const [params, setParams] = useSearchParams()
  const [search, setSearch] = useState(params.get('q') ?? '')
  const [category, setCategory] = useState(params.get('category') ?? 'all')
  const [status, setStatus] = useState(params.get('status') ?? 'all')
  const [page, setPage] = useState(1)
  const query = useQuery({ queryKey: ['skills'], queryFn: api.skills })
  useEffect(() => { const handle = setTimeout(() => { const next = new URLSearchParams(); if (search) next.set('q', search); if (category !== 'all') next.set('category', category); if (status !== 'all') next.set('status', status); setParams(next, { replace: true }) }, 300); return () => clearTimeout(handle) }, [search, category, status, setParams])
  const filtered = useMemo(() => query.data?.filter((skill) => {
    const haystack = `${skill.displayName} ${skill.name} ${skill.description} ${skill.owner.name}`.toLowerCase()
    return haystack.includes(search.toLowerCase()) && (category === 'all' || skill.category.id === category) && (status === 'all' || skill.status === status)
  }) ?? [], [query.data, search, category, status])
  const categories = Array.from(new Map(query.data?.map((s) => [s.category.id, s.category.name])).entries())
  return <>
    <PageHeader title="Skill Catalog" subtitle="发现、评估并复用经过治理的研发 AI 能力" />
    <Card className="filter-card">
      <Input size="large" prefix={<SearchOutlined />} allowClear placeholder="搜索 AI 工装名称、能力或 Owner" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1) }} />
      <div className="filter-row"><Select value={category} onChange={setCategory} options={[{ value: 'all', label: '全部能力域' }, ...categories.map(([value, label]) => ({ value, label }))]} /><Select value={status} onChange={setStatus} options={[{ value: 'all', label: '全部状态' }, ...Object.entries(statusLabels).map(([value, label]) => ({ value, label }))]} /><Select defaultValue="updated" options={[{ value: 'updated', label: '最近更新' }, { value: 'name', label: '名称排序' }, { value: 'relevance', label: '相关度' }]} /><span className="result-count">共 {filtered.length} 项工装</span></div>
    </Card>
    {query.isLoading ? <LoadingState /> : query.isError ? <ErrorState error={query.error} retry={() => query.refetch()} /> : filtered.length === 0 ? <Card><Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={<><strong>没有找到匹配的 AI 工装</strong><br /><Text type="secondary">请调整关键词或清除筛选条件</Text></>}><Button onClick={() => { setSearch(''); setCategory('all'); setStatus('all') }}>清除筛选</Button></Empty></Card> : <><Row gutter={[20, 20]}>{filtered.slice((page - 1) * 24, page * 24).map((skill) => <Col span={8} key={skill.id}><SkillCard skill={skill} /></Col>)}</Row><Pagination className="page-pagination" current={page} pageSize={24} total={filtered.length} onChange={setPage} showSizeChanger={false} /></>}
  </>
}

function SkillDetailPage() {
  const { name = '' } = useParams()
  const [selectedVersion, setSelectedVersion] = useState('')
  const [installOpen, setInstallOpen] = useState(false)
  const query = useQuery({ queryKey: ['skill', name], queryFn: () => api.skill(name) })
  useEffect(() => { if (query.data) setSelectedVersion(query.data.recommendedVersion) }, [query.data])
  if (query.isLoading) return <LoadingState />
  if (query.isError) return <ErrorState error={query.error} retry={() => query.refetch()} />
  const skill = query.data!
  const current = skill.versions.find((v) => v.version === selectedVersion) ?? skill.versions[0]
  const reviewItems = skill.reviewHistory?.length ? skill.reviewHistory.flatMap((entry) => [
    {
      color: 'blue',
      children: <div><strong>{entry.submitter}</strong> 提交 {entry.version}<br /><Text type="secondary">{new Date(entry.submittedAt).toLocaleString('zh-CN')}</Text></div>,
    },
    ...(entry.decidedAt ? [{
      color: entry.decision === 'approved' ? 'green' : 'red',
      dot: entry.decision === 'approved' ? <CheckCircleFilled /> : <StopOutlined />,
      children: <div><strong>{entry.reviewer ?? '审核人'}</strong> {entry.decision === 'approved' ? `批准发布为 ${entry.channel}` : '拒绝发布'}{entry.comment && <><br />{entry.comment}</>}<br /><Text type="secondary">{new Date(entry.decidedAt).toLocaleString('zh-CN')}</Text></div>,
    }] : []),
  ]) : skill.versions.map((version) => ({
    color: version.status === 'stable' ? 'green' : 'blue',
    children: <div><strong>{version.version}</strong> · {statusLabels[version.status]}<br /><Text type="secondary">{new Date(version.publishedAt).toLocaleString('zh-CN')}</Text></div>,
  }))
  return <>
    <Breadcrumb items={[{ title: <Link to="/skills">Skill Catalog</Link> }, { title: skill.displayName }]} className="detail-breadcrumb" />
    <Card className="detail-hero"><div className="detail-title"><Avatar shape="square" size={64} className="skill-avatar"><CodeOutlined /></Avatar><div><Space><h1>{skill.displayName}</h1><StatusTag status={current.status} /></Space><p>{skill.namespace}/{skill.name}</p><Space><span><TeamOutlined /> {skill.owner.name}</span><span>·</span><span>{skill.category.name}</span><span>·</span><RiskTag risk={skill.riskLevel} /></Space></div></div><div className="detail-actions"><Select value={selectedVersion} onChange={setSelectedVersion} options={skill.versions.map((v) => ({ value: v.version, label: `${v.version} (${statusLabels[v.status]})` }))} /><Button icon={<CopyOutlined />} onClick={() => navigator.clipboard.writeText(`${skill.namespace}/${skill.name}@${selectedVersion}`).then(() => message.success('已复制 Skill 引用'))}>复制引用</Button><Button type="primary" icon={<CodeOutlined />} onClick={() => setInstallOpen(true)} disabled={current.status === 'revoked'}>CLI 安装</Button></div></Card>
    {current.status === 'beta' && <Alert type="warning" showIcon message="当前为测试版本" description="安装命令需要增加 --allow-non-stable，并关注后续权限或行为变化。" />}
    {current.status === 'deprecated' && <Alert type="error" showIcon message="该版本已停止推荐" description="已安装版本可继续使用，新安装前必须确认风险。" />}
    <Tabs className="detail-tabs" items={[
      { key: 'readme', label: '工装说明', children: <Card className="markdown-card"><ReactMarkdown>{skill.skillMarkdown}</ReactMarkdown></Card> },
      { key: 'permissions', label: '权限与风险', children: <Row gutter={20}><Col span={15}><Card title="权限声明"><PermissionPanel permissions={skill.permissions} /></Card></Col><Col span={9}><Card title="安全摘要"><Descriptions column={1} size="small" items={[{ key: 'risk', label: '风险等级', children: <RiskTag risk={skill.riskLevel} /> }, { key: 'digest', label: '内容摘要', children: <Text code copyable>{current.digest}</Text> }, { key: 'runtime', label: '兼容运行时', children: skill.runtime.join('、') }, { key: 'review', label: '审核状态', children: <StatusTag status={current.status} /> }]} /></Card></Col></Row> },
      { key: 'versions', label: `版本 (${skill.versions.length})`, children: <Card><Timeline items={skill.versions.map((v) => ({ color: v.status === 'stable' ? 'green' : 'blue', children: <div className="version-item"><div><Space><strong>{v.version}</strong><StatusTag status={v.status} /></Space><p>{v.changelog}</p><small>{new Date(v.publishedAt).toLocaleString('zh-CN')} · {v.fileCount} 个文件 · {(v.sizeBytes / 1024).toFixed(1)} KB</small></div><Text code copyable>{v.digest}</Text></div> }))} /></Card> },
      { key: 'files', label: `文件 (${skill.files.length})`, children: <Card><Table pagination={false} dataSource={skill.files} rowKey="path" columns={[{ title: '文件路径', dataIndex: 'path', render: (value) => <Space><FileMarkdownOutlined />{value}</Space> }, { title: '大小', dataIndex: 'sizeBytes', width: 120, render: (value) => `${(value / 1024).toFixed(1)} KB` }, { title: 'SHA-256', dataIndex: 'sha256', render: (value) => <Text code copyable>{value}</Text> }, { title: '操作', width: 100, render: () => <Button type="link">预览</Button> }]} /></Card> },
      { key: 'review', label: '审核记录', children: <Card>{reviewItems.length ? <Timeline items={reviewItems} /> : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="暂无审核记录" />}</Card> },
    ]} />
    <InstallDrawer skill={skill} version={selectedVersion} open={installOpen} onClose={() => setInstallOpen(false)} />
  </>
}

function InstallDrawer({ skill, version, open, onClose }: { skill: SkillSummary; version?: string; open: boolean; onClose: () => void }) {
  const [selectedVersion, setSelectedVersion] = useState(version ?? skill.recommendedVersion)
  const [agent, setAgent] = useState<CliAgentId>('all')
  useEffect(() => { if (version) setSelectedVersion(version) }, [version])
  const reference = createSkillReference(skill.namespace, skill.name, selectedVersion)
  const commands = createCliCommandSet(reference, agent)
  const selectedStatus = skill.versions.find((item) => item.version === selectedVersion)?.status
  const installCommand = selectedStatus === 'stable' ? commands.install : `${commands.install} --allow-non-stable`
  return <Drawer title="Agent Skill Store CLI" width={560} open={open} onClose={onClose} footer={<div className="drawer-footer"><Button onClick={onClose}>关闭</Button><Button type="primary" icon={<CopyOutlined />} onClick={() => copyCliCommand(installCommand)}>复制安装命令</Button></div>}>
    <Form layout="vertical">
      <Form.Item label="AI 工装"><Input value={`${skill.namespace}/${skill.name}`} disabled /></Form.Item>
      <Form.Item label="目标 Agent"><Select value={agent} onChange={setAgent} options={cliAgentOptions} /></Form.Item>
      <Form.Item label="版本"><Select value={selectedVersion} onChange={setSelectedVersion} options={skill.versions.filter((v) => !['draft', 'pending', 'revoked'].includes(v.status)).map((v) => ({ value: v.version, label: `${v.version} · ${statusLabels[v.status]}` }))} /></Form.Item>
    </Form>
    <div className="cli-command-stack">
      <CliCommandLine command={installCommand} />
      <CliCommandLine command={commands.update} />
      <CliCommandLine command={commands.uninstall} />
      <CliCommandLine command={commands.rollback} />
    </div>
    <Card size="small" title="权限声明"><PermissionPanel permissions={skill.permissions} compact /></Card>
  </Drawer>
}

function CliPage() {
  return <><PageHeader title="Agent Skill Store CLI" subtitle="使用共享 .agents 目录，并为非原生 Agent 管理桥接" extra={<Button type="primary" icon={<AppstoreOutlined />} onClick={() => location.assign('/skills')}>浏览工装</Button>} /><Row gutter={[20, 20]}><Col span={16}><CliWorkbench /></Col><Col span={8}><Card title="常用命令" className="cli-side-card"><CliCommandLine command="skillstore login --server <skill-store-url> --api-key <key>" /><CliCommandLine command="skillstore list --output json" /><CliCommandLine command="skillstore list installed" /><CliCommandLine command="skillstore doctor" /></Card></Col></Row></>
}

function PublishPage() {
  const [form] = Form.useForm()
  const [stage, setStage] = useState(0)
  const [packageFile, setPackageFile] = useState<File | undefined>()
  const mutation = useMutation({ mutationFn: api.createPublication, onSuccess: () => { setStage(3); message.success('发布草稿已创建并提交审核') }, onError: (error: Error) => message.error(error.message) })
  const submit = async () => { const values = await form.validateFields(); mutation.mutate({ ...values, package: packageFile }) }
  return <><PageHeader title="发布中心" subtitle="上传、校验并提交新的 AI 工装版本" extra={<Button>我的发布</Button>} />
    <Card><Steps current={stage} items={[{ title: '上传工装包', description: 'ZIP 或目录' }, { title: '结构校验', description: '规范与安全' }, { title: '治理信息', description: '权限与风险' }, { title: '提交审核', description: '冻结包内容' }]} />
      <Divider />
      {stage === 0 && <div className="upload-stage"><Upload.Dragger accept=".zip" beforeUpload={(file) => { setPackageFile(file); return false }} onRemove={() => { setPackageFile(undefined); return true }} maxCount={1}><p className="ant-upload-drag-icon"><InboxOutlined /></p><p className="ant-upload-text">拖拽 Skill ZIP 到此处，或点击选择文件</p><p className="ant-upload-hint">必须包含 SKILL.md 与 manifest.yaml；单包不超过 100 MB；禁止绝对路径、符号链接逃逸和 ZIP Bomb</p></Upload.Dragger><Button type="primary" size="large" disabled={!packageFile} onClick={() => setStage(1)}>开始校验</Button></div>}
      {stage === 1 && <div className="validation-stage"><Progress percent={20} status="active" /><List dataSource={['已完成 ZIP 类型和客户端大小预检', '提交时由服务端检查路径穿越与符号链接', '提交时由服务端限制文件数、解压大小和压缩比', '提交时由服务端解析 SKILL.md 与 manifest.yaml', '提交时由服务端扫描敏感信息并计算 SHA-256']} renderItem={(item, index) => <List.Item><Space>{index === 0 ? <CheckCircleFilled className="success-icon" /> : <FileSearchOutlined className="pending-icon" />}{item}</Space></List.Item>} /><Alert type="info" showIcon message="安全校验以后端结果为准" description="浏览器不会将未完成的客户端检查标记为安全通过。填写治理信息后，服务端将在创建审核单前完成全部验证。" /><div className="stage-actions"><Button onClick={() => setStage(0)}>重新选择</Button><Button type="primary" onClick={() => setStage(2)}>填写治理信息</Button></div></div>}
      {stage === 2 && <Form form={form} layout="vertical" className="publish-form" initialValues={{ namespace: 'engineering', version: '1.0.0', owner: 'Developer Experience', riskLevel: 'medium', networkPolicy: 'deny-all' }}>
        <Row gutter={20}><Col span={12}><Form.Item label="Skill 名称" name="name" rules={[{ required: true }, { pattern: /^[a-z0-9-]{2,64}$/, message: '使用 2—64 位小写字母、数字和连字符' }]}><Input placeholder="release-note-writer" /></Form.Item></Col><Col span={12}><Form.Item label="显示名称" name="displayName" rules={[{ required: true }]}><Input placeholder="发布说明生成 Skill" /></Form.Item></Col></Row>
        <Row gutter={20}><Col span={8}><Form.Item label="命名空间" name="namespace" rules={[{ required: true }]}><Select options={[{ value: 'engineering', label: 'engineering · 工程效能' }, { value: 'quality', label: 'quality · 质量保障' }, { value: 'product', label: 'product · 产品研发' }]} /></Form.Item></Col><Col span={8}><Form.Item label="版本" name="version" rules={[{ required: true }, { pattern: /^\d+\.\d+\.\d+(-[a-z0-9.-]+)?$/, message: '请输入合法 SemVer' }]}><Input /></Form.Item></Col><Col span={8}><Form.Item label="Owner 团队" name="owner" rules={[{ required: true }]}><Select options={[{ value: 'Platform Team' }, { value: 'Developer Experience' }, { value: 'Quality Engineering' }]} /></Form.Item></Col></Row>
        <Row gutter={20}><Col span={12}><Form.Item label="风险等级" name="riskLevel" rules={[{ required: true }]}><Segmented block options={[{ value: 'low', label: '低' }, { value: 'medium', label: '中' }, { value: 'high', label: '高' }, { value: 'critical', label: '严重' }]} /></Form.Item></Col><Col span={12}><Form.Item label="网络策略" name="networkPolicy" rules={[{ required: true }]}><Select options={[{ value: 'deny-all', label: '默认禁止网络访问' }, { value: 'allow-list', label: '仅允许白名单' }, { value: 'unrestricted', label: '不受限（高风险）' }]} /></Form.Item></Col></Row>
        <Form.Item label="更新日志" name="changelog" rules={[{ required: true }]}><Input.TextArea rows={4} placeholder="说明本版本新增、变更和修复内容" /></Form.Item>
        <Alert type="warning" showIcon message="提交审核后包内容将被冻结" description="如需修改文件或权限，需要创建新草稿或新版本。stable 必须由非作者审核人明确批准。" />
        <div className="stage-actions"><Button onClick={() => setStage(1)}>返回预检说明</Button><Button type="primary" loading={mutation.isPending} onClick={submit}>服务端校验并提交审核</Button></div>
      </Form>}
      {stage === 3 && <Result status="success" title="已提交审核" subTitle="审核管理员将核对包差异、权限声明、扫描结果和风险等级。你可以在“我的发布”中查看进度。" extra={<Button type="primary" onClick={() => { form.resetFields(); setStage(0) }}>继续发布</Button>} />}
    </Card>
  </>
}

function ReviewsPage({ user }: { user: SessionUser }) {
  const queryClient = useQueryClient()
  const query = useQuery({ queryKey: ['reviews'], queryFn: api.reviews })
  const [selected, setSelected] = useState<ReviewItem | null>(null)
  const [comment, setComment] = useState('')
  const [channel, setChannel] = useState<'beta' | 'stable'>('beta')
  const [confirmedPermissions, setConfirmedPermissions] = useState<string[]>([])
  const mutation = useMutation({ mutationFn: ({ decision }: { decision: 'approve' | 'reject' }) => api.decideReview(selected!.id, decision, comment, channel), onSuccess: () => { message.success('审核决定已记录'); setSelected(null); setComment(''); setConfirmedPermissions([]); queryClient.invalidateQueries({ queryKey: ['reviews'] }) } })
  useEffect(() => { if (!selected && query.data?.length) setSelected(query.data[0]) }, [query.data, selected])
  useEffect(() => { setConfirmedPermissions([]) }, [selected?.id])
  const requiredConfirmations = selected && requiresPermissionConfirmation(selected.riskLevel)
    ? createPermissionConfirmations(selected.permissions)
    : []
  const permissionsConfirmed = hasConfirmedAll(requiredConfirmations, confirmedPermissions)
  return <><PageHeader title="审核中心" subtitle="确认版本差异、权限风险和发布通道；禁止审核本人提交" />
    {query.isLoading ? <LoadingState /> : query.isError ? <ErrorState error={query.error} retry={() => query.refetch()} /> : <div className="review-layout"><Card className="review-list" title={`待审核 ${query.data?.length ?? 0}`}><List dataSource={query.data} renderItem={(item) => <List.Item className={selected?.id === item.id ? 'selected-review' : ''} onClick={() => setSelected(item)}><List.Item.Meta avatar={<Avatar shape="square"><AuditOutlined /></Avatar>} title={<Space>{item.displayName}<RiskTag risk={item.riskLevel} /></Space>} description={<><div>{item.skillName}@{item.version}</div><small>{item.submitter} · {new Date(item.submittedAt).toLocaleString('zh-CN')}</small></>} /></List.Item>} /></Card>
      <Card className="review-detail" title={selected ? `${selected.displayName} · ${selected.version}` : '选择待审版本'}>{!selected ? <Empty /> : <>
        {selected.submitter === user.displayName && <Alert type="error" showIcon message="职责分离限制" description="你不能审核本人提交的版本。" />}
        <Descriptions bordered size="small" column={2} items={[{ key: 'skill', label: 'Skill ID', children: selected.skillName }, { key: 'version', label: '版本', children: selected.version }, { key: 'submitter', label: '提交人', children: selected.submitter }, { key: 'risk', label: '风险', children: <RiskTag risk={selected.riskLevel} /> }]} />
        <Divider orientation="left">权限确认</Divider><PermissionPanel permissions={selected.permissions} />
        {requiresPermissionConfirmation(selected.riskLevel) && <div className="risk-confirmation"><Alert type="warning" showIcon message="高风险权限需要逐项确认" description="批准前必须确认每一项请求范围；未完成确认时不能发布。" />{requiredConfirmations.length > 0 ? <Checkbox.Group value={confirmedPermissions} onChange={(values) => setConfirmedPermissions(values.map(String))} options={requiredConfirmations} /> : <Alert type="info" showIcon message="该版本未声明额外权限" />}</div>}
        <Divider orientation="left">审核决定</Divider><Form layout="vertical"><Form.Item label="发布通道"><Segmented block value={channel} onChange={(value) => setChannel(value as 'beta' | 'stable')} options={[{ value: 'beta', label: 'Beta 测试版' }, { value: 'stable', label: 'Stable 认证版' }]} /></Form.Item><Form.Item label="审核意见" required><Input.TextArea rows={4} value={comment} onChange={(e) => setComment(e.target.value)} placeholder="说明通过依据或拒绝原因" /></Form.Item></Form>
        <div className="review-actions"><Button danger disabled={!comment} loading={mutation.isPending} onClick={() => mutation.mutate({ decision: 'reject' })}>拒绝</Button><Button type="primary" icon={<CheckOutlined />} disabled={!comment || selected.submitter === user.displayName || !permissionsConfirmed} loading={mutation.isPending} onClick={() => mutation.mutate({ decision: 'approve' })}>通过并发布为 {channel}</Button></div>
      </>}</Card></div>}
  </>
}

function AdminPage() {
  const audit = useQuery({ queryKey: ['audit'], queryFn: api.audit })
  const columns: TableProps<AuditEvent>['columns'] = [
    { title: '时间', dataIndex: 'createdAt', width: 190, render: (value) => new Date(value).toLocaleString('zh-CN') }, { title: '操作者', dataIndex: 'actor', width: 120 }, { title: '角色', dataIndex: 'role', width: 130 }, { title: '操作', dataIndex: 'action' }, { title: '资源', dataIndex: 'resource' }, { title: '结果', dataIndex: 'result', width: 100, render: (value) => <Tag color={value === 'success' ? 'green' : 'red'}>{value}</Tag> }, { title: 'Request ID', dataIndex: 'requestId', width: 130, render: (value) => <Text code>{value}</Text> },
  ]
  const governanceTabs = [
    { key: 'audit', label: <span><AuditOutlined />审计日志</span>, children: audit.isLoading ? <LoadingState /> : <Card><div className="table-toolbar"><Input prefix={<SearchOutlined />} placeholder="搜索操作者、资源或 Request ID" /><Space><Select defaultValue="all" options={[{ value: 'all', label: '全部结果' }, { value: 'success', label: '成功' }, { value: 'denied', label: '拒绝' }]} /><Button icon={<ReloadOutlined />}>刷新</Button></Space></div><Table rowKey="id" dataSource={audit.data} columns={columns} scroll={{ x: 1080 }} /></Card> },
    { key: 'categories', label: <span><DeploymentUnitOutlined />分类</span>, children: <CategoryPanel /> },
    { key: 'teams', label: <span><TeamOutlined />团队</span>, children: <TeamPanel /> },
    { key: 'keys', label: <span><KeyOutlined />访问凭证</span>, children: <ApiKeyPanel /> },
    { key: 'cli', label: <span><CodeOutlined />CLI 接入</span>, children: <SystemPanel /> },
  ]
  return <><PageHeader title="治理中心" subtitle="管理分类、团队、访问凭证、审计与 CLI 使用入口" /><Tabs items={governanceTabs} /></>
}

function CategoryPanel() {
  const [open, setOpen] = useState(false)
  const [form] = Form.useForm()
  const queryClient = useQueryClient()
  const query = useQuery({ queryKey: ['categories'], queryFn: api.categories })
  const save = useMutation({ mutationFn: api.saveCategory, onSuccess: () => { setOpen(false); form.resetFields(); queryClient.invalidateQueries({ queryKey: ['categories'] }); message.success('分类已保存') } })
  const edit = (record?: { id: string; name: string; sortOrder: number }) => { form.setFieldsValue(record ?? { id: '', name: '', sortOrder: 50 }); setOpen(true) }
  return <Card title="能力域分类" extra={<Button type="primary" icon={<PlusOutlined />} onClick={() => edit()}>新增分类</Button>}>
    <Table loading={query.isLoading} pagination={false} rowKey="id" dataSource={query.data} columns={[{ title: '编码', dataIndex: 'id' }, { title: '名称', dataIndex: 'name' }, { title: '排序', dataIndex: 'sortOrder' }, { title: '操作', render: (_, record) => <Button type="link" onClick={() => edit(record)}>编辑</Button> }]} />
    <Modal title="分类信息" open={open} onCancel={() => setOpen(false)} onOk={() => form.validateFields().then((values) => save.mutate({ ...values, sortOrder: Number(values.sortOrder) }))} confirmLoading={save.isPending}><Form form={form} layout="vertical"><Form.Item label="编码" name="id" rules={[{ required: true }, { pattern: /^[a-z0-9-]+$/ }]}><Input /></Form.Item><Form.Item label="名称" name="name" rules={[{ required: true }]}><Input type="text" /></Form.Item><Form.Item label="排序" name="sortOrder" rules={[{ required: true }]}><Input type="number" /></Form.Item></Form></Modal>
  </Card>
}

function TeamPanel() {
  const [open, setOpen] = useState(false)
  const [form] = Form.useForm()
  const queryClient = useQueryClient()
  const query = useQuery({ queryKey: ['teams'], queryFn: api.teams })
  const save = useMutation({ mutationFn: api.saveTeam, onSuccess: () => { setOpen(false); form.resetFields(); queryClient.invalidateQueries({ queryKey: ['teams'] }); message.success('团队已保存') } })
  const edit = (record?: { id: string; name: string; code: string; status: string }) => { form.setFieldsValue(record ?? { id: `team-${Date.now()}`, name: '', code: '', status: 'active' }); setOpen(true) }
  return <Card title="Owner 团队" extra={<Button type="primary" icon={<PlusOutlined />} onClick={() => edit()}>新增团队</Button>}>
    <List loading={query.isLoading} dataSource={query.data} renderItem={(item) => <List.Item actions={[<Button type="link" key="edit" onClick={() => edit(item)}>编辑</Button>]}><List.Item.Meta avatar={<Avatar icon={<TeamOutlined />} />} title={item.name} description={`${item.code} · ${item.status === 'active' ? '启用' : '停用'} · 可作为工装 Owner`} /></List.Item>} />
    <Modal title="团队信息" open={open} onCancel={() => setOpen(false)} onOk={() => form.validateFields().then((values) => save.mutate(values))} confirmLoading={save.isPending}><Form form={form} layout="vertical"><Form.Item label="ID" name="id" hidden><Input /></Form.Item><Form.Item label="团队名称" name="name" rules={[{ required: true }]}><Input /></Form.Item><Form.Item label="团队编码" name="code" rules={[{ required: true }, { pattern: /^[a-z0-9-]+$/ }]}><Input /></Form.Item><Form.Item label="状态" name="status"><Select options={[{ value: 'active', label: '启用' }, { value: 'inactive', label: '停用' }]} /></Form.Item></Form></Modal>
  </Card>
}

function ApiKeyPanel() {
  const [created, setCreated] = useState('')
  const queryClient = useQueryClient()
  const keys = useQuery({ queryKey: ['api-keys'], queryFn: api.apiKeys })
  const create = useMutation({ mutationFn: () => api.createApiKey('Agent Key'), onSuccess: (result) => { setCreated(result.key); queryClient.invalidateQueries({ queryKey: ['api-keys'] }) } })
  const remove = useMutation({ mutationFn: api.deleteApiKey, onSuccess: () => queryClient.invalidateQueries({ queryKey: ['api-keys'] }) })
  return <Card title="API Key" extra={<Button type="primary" icon={<PlusOutlined />} loading={create.isPending} onClick={() => create.mutate()}>创建凭证</Button>}>
    {created && <Alert closable onClose={() => setCreated('')} type="success" showIcon message="API Key 已创建，仅显示一次" description={<Text code copyable>{created}</Text>} />}
    <Table className="spaced-table" loading={keys.isLoading} pagination={false} rowKey="id" dataSource={keys.data} columns={[{ title: '标签', dataIndex: 'label' }, { title: 'Agent ID', dataIndex: 'agentId', render: (value) => <Text code>{value}</Text> }, { title: '权限', dataIndex: 'scopes', render: (value: string[]) => value.join(' ') }, { title: '创建日期', dataIndex: 'createdAt', render: (value) => new Date(value).toLocaleDateString('zh-CN') }, { title: '过期时间', dataIndex: 'expiresAt', render: (value) => value ? new Date(value).toLocaleDateString('zh-CN') : '永不过期' }, { title: '操作', render: (_, row) => <Button type="link" danger icon={<DeleteOutlined />} loading={remove.isPending} onClick={() => remove.mutate(row.id)}>撤销</Button> }]} />
  </Card>
}

function SystemPanel() {
  return <Row gutter={[20, 20]}><Col span={16}><CliWorkbench /></Col><Col span={8}><Card title="目标 Agent"><List dataSource={cliAgentTargets} renderItem={(target) => <List.Item><List.Item.Meta title={target.label} description={<Text code>{target.skillRoot}</Text>} /></List.Item>} /></Card></Col></Row>
}

function AboutPage() {
  return <><PageHeader title="关于 Agent Skill Store" subtitle="Discover. Govern. Install." />
    <Card className="about-card"><div className="about-brand"><LogoLockup /><h2>Discover. Govern. Install.</h2><p>一个可自托管的 Agent Skill 目录、治理后台和 CLI 安装入口，支持公开发现、独立审核、版本管理与本机安装。</p></div><Divider /><Descriptions column={{ xs: 1, sm: 1, md: 2 }} bordered items={[{ key: 'product', label: '项目名称', children: 'Agent Skill Store' }, { key: 'cli', label: 'CLI 命令', children: 'skillstore' }, { key: 'version', label: '当前版本', children: 'v0.2.0' }, { key: 'license', label: '开源许可', children: 'Apache License 2.0' }, { key: 'access', label: '访问模型', children: '公开浏览 · 受控发布' }, { key: 'deployment', label: '部署方式', children: 'Self-hosted' }]} /><Alert type="info" showIcon message="API Key 安全模型" description="Bootstrap Key 由 CLI 离线生成，服务端只保存 SHA-256；普通 Agent Key 的原文仅在创建时返回一次。" /><div className="about-links"><Button href="/.well-known/agent-skills/index.json" icon={<FileMarkdownOutlined />}>发现索引</Button><Button href="/manifest.json" icon={<CodeOutlined />}>原生清单</Button><Button href="/health" icon={<SafetyCertificateOutlined />}>服务状态</Button></div></Card>
  </>
}

export default function App() {
  const [user, setUser] = useState<SessionUser | null | undefined>(undefined)
  const location = useLocation()
  const navigate = useNavigate()
  useEffect(() => { api.session().then(setUser) }, [])
  const logout = async () => { await api.logout(); setUser(null); navigate('/') }
  if (user === undefined) return <div className="app-boot"><LogoLockup /><Progress type="circle" percent={70} size={48} showInfo={false} /></div>
  if (!user && location.pathname === '/login') return <LoginPage onLogin={(nextUser) => { setUser(nextUser); navigate('/') }} />
  return <AppShell user={user} onLogout={logout} />
}
