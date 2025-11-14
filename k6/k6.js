import http from 'k6/http';
import { check } from 'k6';

export let options = {
  vus: 50,        // usuários virtuais
  duration: '1m', // 1 minuto
};

export default function () {
  let payload = JSON.stringify({
    Descricao: "Teste",
    Valor: 100,
    Tipo: "C",
    DataLancamento: new Date().toISOString()
  });

  let res = http.post('http://localhost:8080/lancamentos', payload, { headers: { 'Content-Type': 'application/json' } });

  check(res, { 'status 200': (r) => r.status === 200 });
}
