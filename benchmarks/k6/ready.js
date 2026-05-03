import http from 'k6/http';

export const options = {
    summaryTrendStats: ['p(99)'],
    systemTags: ['status', 'method'],
    scenarios: {
        default: {
            executor: 'ramping-arrival-rate',
            startRate: __ENV.K6_START_RATE ? Number(__ENV.K6_START_RATE) : 900,
            timeUnit: '1s',
            preAllocatedVUs: __ENV.K6_PRE_ALLOCATED_VUS ? Number(__ENV.K6_PRE_ALLOCATED_VUS) : 100,
            maxVUs: __ENV.K6_MAX_VUS ? Number(__ENV.K6_MAX_VUS) : 250,
            gracefulStop: __ENV.K6_GRACEFUL_STOP || '2s',
            stages: [
                { duration: __ENV.K6_STAGE_DURATION || '12s', target: __ENV.K6_TARGET_RATE ? Number(__ENV.K6_TARGET_RATE) : 900 },
            ],
        },
    },
};

const url = __ENV.K6_URL || 'http://lb:9999/ready';

export default function () {
    http.get(url, { timeout: '2001ms' });
}

export function handleSummary(data) {
    const p99 = data.metrics.http_req_duration.values['p(99)'];
    const result = {
        p99: p99.toFixed(2) + 'ms',
        http_reqs: data.metrics.http_reqs ? data.metrics.http_reqs.values.count : 0,
        http_req_failed: data.metrics.http_req_failed ? data.metrics.http_req_failed.values.rate : 0,
    };

    return {
        'test/results.json': JSON.stringify(result, null, 2),
    };
}
