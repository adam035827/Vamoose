import functools
import logging

from .config import Settings

logger = logging.getLogger(__name__)


@functools.lru_cache(maxsize=8)
def read_secure_parameter(name: str, region: str) -> str:
    # Uses the default AWS credentials: the EC2 instance role on servers, AWS_PROFILE on laptops.
    import boto3

    response = boto3.client("ssm", region_name=region).get_parameter(Name=name, WithDecryption=True)
    return response["Parameter"]["Value"]


def tavily_api_key(settings: Settings) -> str | None:
    """TAVILY_API_KEY wins; otherwise the SecureString named by VAMOOSE_TAVILY_PARAMETER; otherwise None."""
    if settings.tavily_api_key:
        return settings.tavily_api_key
    if not settings.tavily_parameter:
        return None
    try:
        return read_secure_parameter(settings.tavily_parameter, settings.aws_region) or None
    except Exception as ex:
        logger.warning("Could not read Tavily key from Parameter Store %s: %s", settings.tavily_parameter, type(ex).__name__)
        return None
