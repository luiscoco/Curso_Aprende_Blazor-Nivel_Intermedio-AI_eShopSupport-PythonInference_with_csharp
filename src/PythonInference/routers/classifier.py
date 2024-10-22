from fastapi import APIRouter
from pydantic import BaseModel
from transformers import pipeline

router = APIRouter()

# Modify to use CPU by setting device=-1
classifier = pipeline('zero-shot-classification', model='cross-encoder/nli-MiniLM2-L6-H768', device=-1, framework='pt', from_pt=True)

# Warm-up (optional)
classifier('warm up', ['a', 'b', 'c'])

class ClassifyRequest(BaseModel):
    text: str
    candidate_labels: list[str]

@router.post("/classify")
def classify_text(item: ClassifyRequest) -> str:
    result = classifier(item.text, item.candidate_labels)
    return result['labels'][0]

