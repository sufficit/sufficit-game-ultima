# Hosted mission API rollout

## Investigation

The published office was already configured for the hosted Ultima API.
That server was running a release from before the mission endpoints.
The deployment had one replica and mounted the existing `world-data` claim
at `/data`. Its full deployment template included that custom mount.

## Changes

- Validated the tracked `app/` source using the Sufficit Run release
  contract and built it in a controlled ASP.NET build job.
- Temporarily freed one ConfigMap quota slot using the payload of a
  completed historical build. Restored the payload and verified its data
  after the new build completed.
- Promoted the new release and kept the existing deployment template and
  data volume. Used a single-writer handover and restored the original
  deployment strategy after validation.
- Provisioned separate author and agent credentials in the Kubernetes
  Secret `ultima-mission-credentials`. The app receives references to
  that Secret. [Connection and role guide](../USAGE-ENVIRONMENTS.md#game-api)

## Validation and delivery

- Controlled build and Release tests passed. The disposable HTTP API suite
  verified role-restricted creation, concurrent event retries, and
  persistence after restart. The garden API regression also passed.
- Production release
  `ccb41cdbb369566298afec1ca4ed7e0a6a686e212ac29c0a32c0f8a023f5eb91`
  reported a healthy service. The office board returned HTTP 401 without
  credentials and HTTP 200 for both roles. Browser CORS preflight returned
  HTTP 204. Chromium rendered the published office, connected to the actual
  API, disconnected, and verified that browser storage did not retain the key.
- The same data volume and normalized garden state were verified before
  and after deployment. Revision 55 contained 32 edits, 19 placements, and
  two tiles. Production verification used read-only requests.
  [Office](https://sufficit.github.io/sufficit-game-ultima/environments/?id=office)
  [Server health](https://w-5bee2eba74174628a92a487c6a938f9b.run.sufficit.com.br/health)
