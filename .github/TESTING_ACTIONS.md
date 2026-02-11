# 🧪 Testando GitHub Actions Localmente

## 📦 Instalação do `act`

### Opção 1: Chocolatey
```powershell
choco install act-cli
```

### Opção 2: Scoop
```powershell
scoop install act
```

### Opção 3: Download manual
Baixe de: https://github.com/nektos/act/releases/latest

### Opção 4: Windows Package Manager (winget)
```powershell
winget install nektos.act
# Aceite os termos quando solicitado
```

## 🔧 Pré-requisitos

- **Docker Desktop** instalado e em execução
- Verificar: `docker --version`

## 🎯 Comandos básicos

### 1. Listar workflows disponíveis
```bash
act -l
```

### 2. Testar workflow de build e testes
```bash
# Simular push na branch main
act push -W .github/workflows/build-test.yml

# Simular pull request
act pull_request -W .github/workflows/build-test.yml
```

### 3. Testar workflow de Docker build
```bash
act push -W .github/workflows/docker-build.yml
```

### 4. Executar job específico
```bash
act -j build-and-test
```

### 5. Executar com secrets (criar arquivo .secrets)
```bash
# Criar arquivo .secrets
# SONAR_TOKEN=seu_token_aqui
# PROD_HOST=seu_host

act --secret-file .secrets
```

### 6. Usar runner diferente (mais rápido)
```bash
# Usar imagem medium (recomendado)
act -P ubuntu-latest=catthehacker/ubuntu:act-latest

# Usar imagem menor
act -P ubuntu-latest=node:16-buster-slim
```

### 7. Modo dry-run (apenas mostrar o que seria executado)
```bash
act -n
```

### 8. Ver logs detalhados
```bash
act -v
```

## ⚙️ Configuração (arquivo .actrc)

Crie um arquivo `.actrc` na raiz do projeto:

```ini
-P ubuntu-latest=catthehacker/ubuntu:act-latest
--container-architecture linux/amd64
```

## 🔒 Testando com secrets

Crie `.github/.secrets` (NÃO commite este arquivo!):

```env
GITHUB_TOKEN=ghp_seu_token_aqui
SONAR_TOKEN=seu_sonar_token
DOCKERHUB_TOKEN=seu_docker_token
PROD_HOST=seu_servidor
PROD_USER=seu_usuario
PROD_SSH_KEY=sua_chave_ssh
```

Adicione ao `.gitignore`:
```
.secrets
.github/.secrets
```

Execute com secrets:
```bash
act --secret-file .github/.secrets
```

## 🚫 Limitações

- **Não suporta**:
  - Alguns runners específicos do GitHub
  - Caches de dependências (parcialmente)
  - Alguns serviços do GitHub (packages, codeql)
  
- **Workarounds**:
  - Use `continue-on-error: true` para steps problemáticos
  - Comente temporariamente steps que não funcionam localmente

## 📝 Exemplos práticos

### Testar apenas o build (sem push do Docker)
```bash
act push -W .github/workflows/docker-build.yml \
  --dryrun \
  -j build-docker
```

### Testar com evento específico
```bash
act push --eventpath .github/test-event.json
```

Crie `.github/test-event.json`:
```json
{
  "ref": "refs/heads/main",
  "repository": {
    "name": "FluxoCaixaApi",
    "owner": {
      "login": "seu-usuario"
    }
  }
}
```

### Ignorar steps problemáticos

Adicione ao workflow temporariamente para testar localmente:
```yaml
- name: Step problemático
  if: ${{ !env.ACT }}
  run: comando_que_só_funciona_no_github
```

## 🐛 Troubleshooting

### Erro: "Cannot connect to Docker daemon"
```powershell
# Certifique-se que Docker Desktop está rodando
docker ps
```

### Erro: "permission denied" no Linux/WSL
```bash
sudo usermod -aG docker $USER
newgrp docker
```

### Workflow muito lento
Use imagens menores:
```bash
act -P ubuntu-latest=node:16-slim
```

### Erro de plataforma no Windows
```bash
act --container-architecture linux/amd64
```

## 🎓 Recursos

- Documentação oficial: https://github.com/nektos/act
- Imagens recomendadas: https://github.com/catthehacker/docker_images
- Troubleshooting: https://github.com/nektos/act/issues

## ✅ Checklist antes do commit

- [ ] `act -l` lista todos os workflows
- [ ] `act push -n` executa sem erros (dry-run)
- [ ] Build local passa: `act -j build-and-test`
- [ ] Docker build funciona: `act -j build-docker` (se tiver Docker)
- [ ] Secrets estão em `.gitignore`
