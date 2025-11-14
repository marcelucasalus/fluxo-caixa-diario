# FluxoCaixa

## Descrição do Projeto
Aplicação backend em .NET para controle de fluxo de caixa diário, com serviços independentes de Lançamentos e Consolidado Diário, garantindo alta disponibilidade, resiliência, segurança e performance.

- **Serviço de Lançamentos:** Responsável por registrar lançamentos (débitos e créditos) e, se não existir um consolidado vinculado, cria um novo. Permite consulta de lançamentos por data específica.  
- **Serviço de Consolidado Diário:** Calcula e fornece saldo diário consolidado.
- **Serviço de Worker:** Roda em background para processar lançamentos pendentes e atualizar o consolidado diário.
- **Serviço de Autenticação:** Permite cadastro de usuários e login com perfis distintos:
  - **Admin:** Pode realizar todos os endpoints.
  - **Consulta:** Apenas pode consultar lançamentos e consolidado.

O sistema utiliza arquitetura de microsserviços, mensageria (RabbitMQ), cache (Redis), logs centralizados (Elastic), segurança via Identity e JWT, e escalabilidade com Docker + Nginx e banco SQL Server.

---

## **Arquitetura da Solução**

### Diagrama de arquitetura
![Diagrama de Arquitetura](diagrama.png)

**Fluxo principal:**
1. Usuário realiza um lançamento via `POST /lancamentos`.
2. O serviço de Lançamentos salva o lançamento no banco.
3. Se o serviço de Consolidado estiver indisponível:
   - O lançamento é registrado como **pendente** e enviado para **RabbitMQ**.
   - Um **Worker** processa os lançamentos pendentes quando o serviço volta.
4. Serviço de Consolidado consome lançamentos e atualiza o saldo diário.
5. Consultas podem ser feitas via:
   - `GET /lancamentos?data=yyyy-MM-dd`
   - `GET /consolidado?data=yyyy-MM-dd`
6. Cache (Redis) otimiza consultas frequentes de saldo.
7. Autenticação e autorização:
   - `POST /auth/register` → cadastra usuário (Admin ou Consulta)  
   - `POST /auth/login` → autentica usuário e retorna token JWT  
   - Perfis determinam acesso aos endpoints.

---

## **Endpoints**

| Método | Endpoint                 | Descrição |
|--------|--------------------------|-----------|
| POST   | `/auth/register`         | Cadastra um novo usuário (Admin ou Consulta) |
| POST   | `/auth/login`            | Autentica usuário e retorna token JWT |
| GET    | `/lancamentos?data=yyyy-MM-dd` | Busca lançamentos por data |
| GET    | `/consolidado?data=yyyy-MM-dd` | Consulta consolidado diário |
| POST   | `/lancamentos`           | Registra novo lançamento; cria consolidado se necessário |

---

## **Tecnologias utilizadas**
- **Backend:** .NET 9 / C#  
- **Banco de dados:** SQL Server  
- **Mensageria:** RabbitMQ  
- **Cache:** Redis  
- **Logs:** Serilog + ElasticSearch  
- **Segurança:** Identity + JWT  
- **Orquestração:** Docker + Docker Compose + Nginx  
- **Testes:** xUnit

---

## **Como rodar localmente**

### Pré-requisitos
- Docker e Docker Compose
- .NET SDK 7 instalado (opcional se for rodar sem containers)

### Passos

1. Clonar o repositório:
```bash
git clone https://github.com/marcelucasalus/-fluxo-caixa-diario
cd fluxocaixa
```
2. Acessar caminho raiz do repositório

3. Executar os comandos do docker-compose:

    - docker-compose build
    - docker-compose up -d sqlserver redis rabbitmq elasticsearch
    - docker-compose up -d fluxocaixaapi nginx

## Descrição do fluxo

### Get Lancamentos
- Consulta cache (Redis)
- Se não existir, consulta SQL Server
- Atualiza cache com o resultado

### Get Consolidado
- Consulta cache
- Se não existir, consulta SQL Server
- Atualiza cache

### Post Lancamentos
- Cria lançamento
- Verifica se consolidado existe:
  - Se existir → vincula lançamento
  - Se não → cria consolidado e vincula
- Caso serviço de consolidado esteja offline:
  - Marca lançamento como pendente
  - Salva no banco e envia para RabbitMQ
- Worker monitora health check:
  - Processa lançamentos pendentes
  - Atualiza consolidado no banco

### Auth/Register e Auth/Login
- `POST /auth/register` → cadastra usuário (Admin ou Consulta)
- `POST /auth/login` → autentica usuário e retorna JWT
- Perfis determinam acesso aos endpoints

### Logs
- Toda operação gera logs enviados para Elasticsearch via Serilog

---

## 🚀 Melhorias Futuras

### 1️⃣ Monitoramento e Observabilidade
- **Prometheus** para coleta de métricas (latência, contagem de requisições, filas pendentes)
- **Grafana** para dashboards interativos e alertas
- **Tracing distribuído (OpenTelemetry)** para rastrear o fluxo completo de lançamentos

### 2️⃣ Orquestração e Escalabilidade
- **Kubernetes** para deploy, escalabilidade e health checks automáticos
- **Horizontal Pod Autoscaling (HPA)** para ajustar réplicas conforme demanda
- **ConfigMaps e Secrets** para gerenciar configurações e senhas com segurança

### 3️⃣ Resiliência e Mensageria
- **Circuit Breaker / Retry Policies** para falhas no SQL Server ou Redis
- **Dead Letter Queue no RabbitMQ** para mensagens que falharem várias vezes

### 4️⃣ Logging e Centralização
- Integração futura com **Loki/Grafana** para centralização de logs
- Alertas automáticos caso worker ou banco falhem

### 5️⃣ CI/CD e Automação
- Pipelines para build, testes e deploy automático (GitHub Actions, GitLab CI/CD ou Azure DevOps)
- Deploy automatizado no Kubernetes com **Helm Charts** ou **Kustomize**


```mermaid

flowchart TD
    %% Ator
    A[Client/API] --> B[Auth Controller: /register & /login]

    %% Autenticação
    B --> C[Token JWT]
    C --> D[Perfis de Usuário: Admin / Consulta]

    %% Endpoints
    D --> E[GET Lancamentos]
    D --> F[GET Consolidado]
    D --> G[POST Lancamentos]

    %% GET Lancamentos
    E --> H{Cache Hit?}
    H -->|Sim| I[Retorna do Redis]
    H -->|Não| J[Consulta SQL Server]
    J --> I[Atualiza Cache]

    %% GET Consolidado
    F --> K{Cache Hit?}
    K -->|Sim| L[Retorna do Redis]
    K -->|Não| M[Consulta SQL Server]
    M --> L[Atualiza Cache]

    %% POST Lancamentos
    G --> N[Cria lançamento]
    N --> O{Consolidado existe?}
    O -->|Sim| P[Atualiza Consolidado no SQL + Cache]
    O -->|Não| Q[Marca como pendente + Salva no SQL + Envia RabbitMQ]
    Q --> R[Worker Background (HealthCheck)]
    R --> S[Atualiza Consolidado no SQL]

    %% Legendas para clareza
    style A fill:#f9f,stroke:#333,stroke-width:1px
    style B fill:#bbf,stroke:#333,stroke-width:1px
    style C fill:#bfb,stroke:#333,stroke-width:1px
    style D fill:#ffb,stroke:#333,stroke-width:1px
    style E fill:#fff,stroke:#333,stroke-width:1px
    style F fill:#fff,stroke:#333,stroke-width:1px
    style G fill:#fff,stroke:#333,stroke-width:1px
    style H fill:#fdd,stroke:#333,stroke-width:1px
    style K fill:#fdd,stroke:#333,stroke-width:1px
    style O fill:#fdd,stroke:#333,stroke-width:1px
```